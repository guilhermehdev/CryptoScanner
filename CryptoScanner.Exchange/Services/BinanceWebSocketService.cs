using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CryptoScanner.Exchange.Services;

public sealed class BinanceWebSocketService : IAsyncDisposable
{
    private const string BaseUrl = "wss://stream.binance.com:9443/ws";

    // Streams de kline do BTC — servem de "relógio" pro scan reagir no instante exato em
    // que um candle novo fecha, em vez de esperar o próximo tick do timer fixo. Assinados
    // uma única vez, de forma permanente, independente de qual perfil está ativo na tela —
    // isso evita ter que reassinar toda vez que o usuário troca de perfil.
    private static readonly string[] KlineStreams = { "btcusdt@kline_1h", "btcusdt@kline_4h" };

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _requestId;

    /// <summary>Disparado a cada atualização de preço recebida (symbol, preço atual).</summary>
    public event Action<string, decimal>? PriceUpdated;

    /// <summary>
    /// Disparado no instante exato em que um candle de BTCUSDT fecha — parâmetro é o
    /// intervalo ("1h" ou "4h"), pra quem escuta decidir se isso é relevante pro perfil
    /// atualmente selecionado.
    /// </summary>
    public event Action<string>? CandleClosed;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(BaseUrl), cancellationToken);

        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(_receiveLoopCts.Token);

        await SendSubscriptionMessageAsync("SUBSCRIBE", KlineStreams, cancellationToken);
    }

    /// <summary>
    /// Ajusta as inscrições de preço (@ticker) pra bater exatamente com o conjunto
    /// desejado — só manda SUBSCRIBE/UNSUBSCRIBE da diferença, sem precisar reconectar.
    /// Não mexe nos streams de kline (esses são permanentes, ver <see cref="KlineStreams"/>).
    /// </summary>
    public async Task SyncSubscriptionsAsync(IEnumerable<string> desiredSymbols, CancellationToken cancellationToken = default)
    {
        var desired = new HashSet<string>(desiredSymbols, StringComparer.OrdinalIgnoreCase);

        var toSubscribe = desired.Where(s => !_subscribedSymbols.Contains(s)).ToList();
        var toUnsubscribe = _subscribedSymbols.Where(s => !desired.Contains(s)).ToList();

        if (toSubscribe.Count > 0)
            await SendSubscriptionMessageAsync("SUBSCRIBE", ToTickerStreams(toSubscribe), cancellationToken);

        if (toUnsubscribe.Count > 0)
            await SendSubscriptionMessageAsync("UNSUBSCRIBE", ToTickerStreams(toUnsubscribe), cancellationToken);

        _subscribedSymbols.Clear();
        foreach (var symbol in desired)
            _subscribedSymbols.Add(symbol);
    }

    private static IEnumerable<string> ToTickerStreams(IEnumerable<string> symbols) =>
        symbols.Select(s => $"{s.ToLowerInvariant()}@ticker");

    private async Task SendSubscriptionMessageAsync(string method, IEnumerable<string> streams, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        var payload = new { method, @params = streams.ToArray(), id = Interlocked.Increment(ref _requestId) };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                {
                    await Task.Delay(2000, cancellationToken);
                    await TryReconnectAsync(cancellationToken);
                    continue;
                }

                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ProcessMessage(Encoding.UTF8.GetString(stream.ToArray()));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Conexão caiu de forma inesperada — espera e tenta reconectar sozinho,
                // em vez de derrubar o app inteiro.
                await Task.Delay(3000, cancellationToken);
                await TryReconnectAsync(cancellationToken);
            }
        }
    }

    private async Task TryReconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(BaseUrl), cancellationToken);

            // Reconectou — precisa re-inscrever tudo que já estava inscrito antes da queda,
            // incluindo os streams de kline (que não fazem parte de _subscribedSymbols).
            await SendSubscriptionMessageAsync("SUBSCRIBE", KlineStreams, cancellationToken);

            if (_subscribedSymbols.Count > 0)
                await SendSubscriptionMessageAsync("SUBSCRIBE", ToTickerStreams(_subscribedSymbols), cancellationToken);
        }
        catch
        {
            // Vai tentar de novo no próximo laço, sem derrubar o app.
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Mensagens de kline têm "e"="kline" e trazem os dados do candle dentro de "k" —
            // formato bem diferente do @ticker, então são tratadas à parte.
            if (root.TryGetProperty("e", out var eventTypeElement) && eventTypeElement.GetString() == "kline")
            {
                ProcessKlineMessage(root);
                return;
            }

            // Mensagens de confirmação de subscribe/unsubscribe não têm "s" (símbolo) —
            // só nos interessa atualização de preço em si.
            if (!root.TryGetProperty("s", out var symbolElement))
                return;

            if (!root.TryGetProperty("c", out var priceElement)) // "c" = último preço no @ticker
                return;

            string symbol = symbolElement.GetString() ?? "";
            if (!decimal.TryParse(priceElement.GetString(), System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                return;

            PriceUpdated?.Invoke(symbol, price);
        }
        catch
        {
            // Mensagem mal formada ou inesperada — ignora e continua recebendo as próximas.
        }
    }

    private void ProcessKlineMessage(JsonElement root)
    {
        if (!root.TryGetProperty("k", out var klineElement))
            return;

        // "x" só vira true no instante exato em que ESSE candle específico fecha — todas
        // as mensagens intermediárias (candle ainda em formação, atualizado a cada negociação)
        // trazem "x"=false e devem ser ignoradas.
        if (!klineElement.TryGetProperty("x", out var closedElement) || closedElement.ValueKind != JsonValueKind.True)
            return;

        if (!klineElement.TryGetProperty("i", out var intervalElement))
            return;

        string interval = intervalElement.GetString() ?? "";
        if (!string.IsNullOrEmpty(interval))
            CandleClosed?.Invoke(interval);
    }

    public async ValueTask DisposeAsync()
    {
        _receiveLoopCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fechando", CancellationToken.None);
            }
            catch
            {
                // Já pode estar fechado/quebrado — ignora.
            }
        }

        _webSocket?.Dispose();
        _receiveLoopCts?.Dispose();
    }
}