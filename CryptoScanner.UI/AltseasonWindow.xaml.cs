using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models.Altseason;
using CryptoScanner.Exchange.Services;
using System.Media;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class AltseasonWindow : Window
{
    private readonly AltseasonLiveDataService _service = new();
    private readonly BinanceWebSocketService _webSocket = new();
    private readonly System.Windows.Threading.DispatcherTimer _baseRefreshTimer = new();
    private decimal _liveBtcPrice;
    private decimal _liveEthPrice;
    private decimal? _lastScore;
    private bool _hasLiveConnection;
    private static readonly decimal[] AlertLevels = [50m, 65m, 75m, 85m];

    public AltseasonWindow()
    {
        InitializeComponent();

        _baseRefreshTimer.Interval = TimeSpan.FromMinutes(1);
        _baseRefreshTimer.Tick += async (_, _) => await RefreshBaseAsync();

        _webSocket.PriceUpdated += OnPriceUpdated;
        Closed += async (_, _) => await DisposeAsync();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshBaseAsync();

        try
        {
            await _webSocket.ConnectAsync();
            await _webSocket.SyncSubscriptionsAsync(["BTCUSDT", "ETHUSDT"]);
            _hasLiveConnection = true;
            txtLive.Text = "● LIVE";
            txtLive.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch
        {
            _hasLiveConnection = false;
            txtLive.Text = "● REST";
            txtLive.Foreground = System.Windows.Media.Brushes.Gold;
        }

        _baseRefreshTimer.Start();
    }

    private void OnPriceUpdated(string symbol, decimal price)
    {
        if (symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase))
            _liveBtcPrice = price;
        else if (symbol.Equals("ETHUSDT", StringComparison.OrdinalIgnoreCase))
            _liveEthPrice = price;
        else
            return;

        if (_liveBtcPrice <= 0m || _liveEthPrice <= 0m)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            var live = _service.UpdateLivePrices(_liveBtcPrice, _liveEthPrice);
            if (live.HasValue)
                ApplyResult(live.Value.Snapshot, live.Value.Score, true);
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshBaseAsync();

    private async Task RefreshBaseAsync()
    {
        btnRefresh.IsEnabled = false;
        try
        {
            var result = await _service.GetAsync();
            _liveBtcPrice = result.Snapshot.BtcPrice;
            _liveEthPrice = result.Snapshot.BtcPrice > 0m && result.Snapshot.EthBtc > 0m
                ? result.Snapshot.BtcPrice * result.Snapshot.EthBtc
                : 0m;

            ApplyResult(result.Snapshot, result.Score, false);
            txtLive.Text = _hasLiveConnection ? "● LIVE" : "● REST";
            txtLive.Foreground = _hasLiveConnection
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Gold;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível atualizar o monitor de altseason.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            btnRefresh.IsEnabled = true;
            UpdateNextRefreshText();
        }
    }

    private void ApplyResult(AltseasonSnapshot snapshot, AltseasonScore score, bool live)
    {
        txtScore.Text = $"{score.Score:F0}/100";
        txtState.Text = score.State;
        txtAction.Text = score.Action;
        txtMarket.Text = $"BTC ${snapshot.BtcPrice:N0}  |  BTC.D {snapshot.BtcDominance:F1}%  |  ETH/BTC {snapshot.EthBtc:G6}  |  TOTAL3 ${snapshot.Total3MarketCap / 1_000_000_000m:N1}B  |  Breadth {snapshot.AltcoinBreadthPercent:F0}%";
        dgIndicators.ItemsSource = score.Indicators;

        txtReference.Text = _service.ReferenceTimestampUtc.HasValue
            ? $"Referência anterior: {_service.ReferenceTimestampUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss} " +
              $"({Math.Max(0, (snapshot.TimestampUtc - _service.ReferenceTimestampUtc.Value).TotalMinutes):F1} min atrás)"
            : "Referência anterior: ainda não existe — esta leitura é o baseline inicial.";

        txtUpdated.Text = live
            ? $"Preço ao vivo {snapshot.TimestampUtc.ToLocalTime():HH:mm:ss}"
            : $"Base atualizada {snapshot.TimestampUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}";

        CheckThresholdCrossing(score.Score);
        UpdateNextRefreshText();
    }

    private void CheckThresholdCrossing(decimal score)
    {
        if (!_lastScore.HasValue)
        {
            _lastScore = score;
            return;
        }

        decimal previous = _lastScore.Value;
        foreach (decimal level in AlertLevels)
        {
            if (previous < level && score >= level)
            {
                TriggerAlert($"ALTSEASON SCORE cruzou {level:0} → {score:F0}/100 ({DescribeLevel(level)})");
                break;
            }

            if (previous >= level && score < level)
            {
                TriggerAlert($"ALTSEASON SCORE perdeu {level:0} → {score:F0}/100 ({DescribeDownLevel(level)})");
                break;
            }
        }

        _lastScore = score;
    }

    private void TriggerAlert(string message)
    {
        txtAlert.Text = $"ALERTA {DateTime.Now:HH:mm:ss} — {message}";
        SystemSounds.Asterisk.Play();
    }

    private static string DescribeLevel(decimal level) => level switch
    {
        50m => "ACUMULAÇÃO",
        65m => "CONFIRMAÇÃO",
        75m => "RISK ON",
        85m => "ALTSEASON",
        _ => ""
    };

    private static string DescribeDownLevel(decimal level) => level switch
    {
        50m => "abaixo da acumulação",
        65m => "perda da confirmação",
        75m => "saída de risk-on",
        85m => "saída de altseason",
        _ => ""
    };

    private void UpdateNextRefreshText()
    {
        txtNextRefresh.Text = _baseRefreshTimer.IsEnabled
            ? "Base REST: ~1 min"
            : "Base REST: iniciando";
    }

    private async Task DisposeAsync()
    {
        _baseRefreshTimer.Stop();
        _webSocket.PriceUpdated -= OnPriceUpdated;
        try
        {
            await _webSocket.DisposeAsync();
        }
        catch
        {
            // Fechamento do stream não deve bloquear a janela.
        }
    }
}
