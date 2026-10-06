using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Configuration;
using System.Windows;

namespace CryptoScanner.UI;

public partial class MainWindow
{
    private static readonly TimeSpan PotentialStrategiesWindow = TimeSpan.FromHours(6);
    private readonly List<TechnicalSetupAlert> _potentialStrategies = [];

    private async void SetupAlertNotifications_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyCryptoScannerTrayIcon();
        _scanner.TechnicalSetupsDetected += OnTechnicalSetupsDetected;
        await RefreshPotentialStrategiesAsync();
    }

    private void SetupAlertNotifications_Closed(object? sender, EventArgs e) =>
        _scanner.TechnicalSetupsDetected -= OnTechnicalSetupsDetected;

    private void OnTechnicalSetupsDetected(IReadOnlyList<TechnicalSetupAlert> alerts)
    {
        if (!IsLoaded)
            return;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            ShowPotentialStrategies(alerts);
            await RefreshPotentialStrategiesAsync();
            await DispatchTechnicalSetupAlertsAsync(alerts);
        });
    }

    private async Task RefreshPotentialStrategiesAsync()
    {
        try
        {
            var storedAlerts = await _scanner.GetTechnicalSetupAlertsAsync(500);
            if (storedAlerts.Count > 0 || _potentialStrategies.Count == 0)
                SetPotentialStrategies(storedAlerts);
        }
        catch (Exception ex)
        {
            txtPotentialStrategiesSummary.Text = $"Não foi possível carregar as estratégias: {ex.Message}";
        }
    }

    private void ShowPotentialStrategies(IReadOnlyList<TechnicalSetupAlert> newAlerts)
    {
        SetPotentialStrategies(newAlerts.Concat(_potentialStrategies));
    }

    private void SetPotentialStrategies(IEnumerable<TechnicalSetupAlert> alerts)
    {
        var orderedAlerts = alerts
            .Where(alert => alert.CandleOpenUtc >= DateTime.UtcNow - PotentialStrategiesWindow)
            .GroupBy(alert => $"{alert.Symbol}|{alert.Direction}|{alert.Setup}|{alert.Profile}|{alert.CandleOpenUtc:O}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(alert => alert.CandleOpenUtc)
            .Take(500)
            .ToList();

        _potentialStrategies.Clear();
        _potentialStrategies.AddRange(orderedAlerts);
        dgPotentialStrategies.ItemsSource = orderedAlerts;
        txtPotentialStrategiesSummary.Text = orderedAlerts.Count == 0
            ? "Nenhuma estratégia potencial detectada nas últimas 6 horas. O grid será preenchido quando o scanner identificar um setup em candle fechado."
            : $"{orderedAlerts.Count} estratégia(s) potencial(is) detectada(s) nas últimas 6 horas. Atualização automática a cada nova detecção.";
    }

    private async void BtnRefreshPotentialStrategies_Click(object sender, RoutedEventArgs e) =>
        await RefreshPotentialStrategiesAsync();

    private void DgPotentialStrategies_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not TechnicalSetupAlert alert)
            return;

        row.IsSelected = true;
        string interval = ToTradingViewInterval(GetHistoryCandleInterval(alert.Profile));
        new ChartWindow(alert.Symbol, interval) { Owner = this }.Show();
    }

    private void BtnSimulatePotentialStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not TechnicalSetupAlert alert)
            return;

        var window = new SimulateTradeWindow(_simulatedTradeRepository, alert,
            () => _priceCheckService.GetCurrentPriceAsync(alert.Symbol))
        {
            Owner = this
        };
        window.ShowDialog();

        if (window.Saved)
            _ = LoadSimulatedTradesAsync();
    }

    private void ApplyCryptoScannerTrayIcon()
    {
        if (_trayIcon is null)
            return;

        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri(
                "pack://application:,,,/CryptoScanner.UI;component/Assets/CryptoScanner.ico",
                UriKind.Absolute));
            if (resource is null)
                return;

            using (resource.Stream)
                _trayIcon.Icon = new System.Drawing.Icon(resource.Stream);
        }
        catch
        {
            // Mantém o ícone padrão caso o recurso não possa ser carregado.
        }
    }

    private async Task DispatchTechnicalSetupAlertsAsync(IReadOnlyList<TechnicalSetupAlert> alerts)
    {
        if (alerts.Count == 0)
            return;

        AlertSettings settings;
        try
        {
            await _alertSettingsRepository.InitializeAsync();
            settings = await _alertSettingsRepository.LoadAsync();
        }
        catch
        {
            return;
        }

        if (!settings.TechnicalSetupAlertsEnabled)
            return;

        string title = alerts.Count == 1
            ? $"Setup identificado: {alerts[0].Symbol}"
            : $"{alerts.Count} setups identificados";
        string body = string.Join("\n", alerts.Take(10).Select(alert =>
            $"{alert.Symbol} — {alert.Setup} | {(alert.Direction == TradeDirection.Long ? "Compra" : "Venda")} | Score {alert.Score:F2} | {alert.Profile}"));
        if (alerts.Count > 10)
            body += $"\n+ {alerts.Count - 10} setup(s)";

        if (settings.DesktopEnabled && _trayIcon != null)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = body.Length > 250 ? body[..250] + "..." : body;
            _trayIcon.ShowBalloonTip(5000);
        }

        var channels = AlertChannelFactory.BuildEnabledChannels(settings);
        if (channels.Count > 0)
            await new AlertDispatcher(channels).SendAsync(title, body);
    }
}
