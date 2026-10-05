using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Configuration;
using System.Windows;

namespace CryptoScanner.UI;

public partial class MainWindow
{
    private void SetupAlertNotifications_Loaded(object sender, RoutedEventArgs e) =>
        _scanner.TechnicalSetupsDetected += OnTechnicalSetupsDetected;

    private void SetupAlertNotifications_Closed(object? sender, EventArgs e) =>
        _scanner.TechnicalSetupsDetected -= OnTechnicalSetupsDetected;

    private void OnTechnicalSetupsDetected(IReadOnlyList<TechnicalSetupAlert> alerts)
    {
        if (!IsLoaded)
            return;

        _ = Dispatcher.InvokeAsync(() => DispatchTechnicalSetupAlertsAsync(alerts));
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
