using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CryptoScanner.Exchange.Services;

public sealed class BinanceWebSocketService : IAsyncDisposable
{
    private const string BaseUrl = "wss://stream.binance.com:9443/ws";

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _requestId;

    /// <summary>Disparado a cada atualização de preço recebida (symbol, preço atual).</summary>
    public event Action<string, decimal>? PriceUpdated;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(BaseUrl), cancellationToken);

        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(_receiveLoopCts.Token);
    }

    /// <summary>
    /// Ajusta as inscrições pra bater exatamente com o conjunto desejado — só manda
    /// SUBSCRIBE/UNSUBSCRIBE da diferença, sem precisar reconectar.
    /// </summary>
    public async Task SyncSubscriptionsAsync(IEnumerable<string> desiredSymbols, CancellationToken cancellationToken = default)
    {
        var desired = new HashSet<string>(desiredSymbols, StringComparer.OrdinalIgnoreCase);

        var toSubscribe = desired.Where(s => !_subscribedSymbols.Contains(s)).ToList();
        var toUnsubscribe = _subscribedSymbols.Where(s => !desired.Contains(s)).ToList();

        if (toSubscribe.Count > 0)
            await SendSubscriptionMessageAsync("SUBSCRIBE", toSubscribe, cancellationToken);

        if (toUnsubscribe.Count > 0)
            await SendSubscriptionMessageAsync("UNSUBSCRIBE", toUnsubscribe, cancellationToken);

        _subscribedSymbols.Clear();
        foreach (var symbol in desired)
            _subscribedSymbols.Add(symbol);
    }

    private async Task SendSubscriptionMessageAsync(string method, List<string> symbols, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return;

        var streams = symbols.Select(s => $"{s.ToLowerInvariant()}@ticker").ToArray();
        var payload = new { method, @params = streams, id = Interlocked.Increment(ref _requestId) };
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

            // Reconectou — precisa re-inscrever tudo que já estava inscrito antes da queda.
            if (_subscribedSymbols.Count > 0)
                await SendSubscriptionMessageAsync("SUBSCRIBE", _subscribedSymbols.ToList(), cancellationToken);
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