using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Configuration;
using System.Windows;

namespace CryptoScanner.UI;

public partial class MainWindow
{
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
            dgPotentialStrategies.ItemsSource = await _scanner.GetTechnicalSetupAlertsAsync(500);
        }
        catch
        {
            // O painel é informativo. Uma falha de leitura não interrompe o scanner.
        }
    }

    private void ShowPotentialStrategies(IReadOnlyList<TechnicalSetupAlert> newAlerts)
    {
        var existing = dgPotentialStrategies.ItemsSource as IEnumerable<TechnicalSetupAlert>
                       ?? Array.Empty<TechnicalSetupAlert>();
        dgPotentialStrategies.ItemsSource = newAlerts
            .Concat(existing)
            .GroupBy(alert => $"{alert.Symbol}|{alert.Direction}|{alert.Setup}|{alert.Profile}|{alert.CandleOpenUtc:O}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(alert => alert.CandleOpenUtc)
            .Take(500)
            .ToList();
    }

    private async void BtnRefreshPotentialStrategies_Click(object sender, RoutedEventArgs e) =>
        await RefreshPotentialStrategiesAsync();

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
