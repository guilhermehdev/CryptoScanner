using CryptoScanner.Infrastructure.Sqlite;
using System.Globalization;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class DatabaseMaintenanceWindow : Window
{
    private readonly SqliteDatabaseMaintenanceService _maintenance;

    public DatabaseMaintenanceWindow(string stateDatabasePath, string candleCachePath)
    {
        InitializeComponent();
        _maintenance = new SqliteDatabaseMaintenanceService(stateDatabasePath, candleCachePath);
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _maintenance.GetSnapshotAsync();
            txtStateDatabase.Text = $"Banco operacional: {FormatBytes(snapshot.StateDatabaseBytes)}";
            txtCandleCache.Text = $"Cache de candles separado: {FormatBytes(snapshot.CandleCacheBytes)}";
            txtPressureHistory.Text = $"Histórico de pressão (IDs máximos): snapshots #{snapshot.PressureSnapshotHighestRowId:N0}; resultados #{snapshot.PressureOutcomeHighestRowId:N0}; preços #{snapshot.PressurePriceHighestRowId:N0}.";
            txtStatus.Text = "";
        }
        catch (Exception ex) { txtStatus.Text = $"Não foi possível ler o armazenamento: {ex.Message}"; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void ClearCandleCache_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remover o cache histórico de candles e compactar os bancos? Sinais, trades, laboratório, LLM e resultados de backtest serão preservados.", "Limpar cache de candles", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunAsync("Limpando cache de candles e compactando os bancos...", () => _maintenance.ClearCandleCachesAndCompactAsync());
    }

    private async void PrunePressure_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remover dados de pressão com mais de 365 dias e compactar o banco? Esta ação apaga somente esse histórico antigo.", "Reduzir histórico de pressão", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunAsync("Reduzindo histórico de pressão...", () => _maintenance.PruneBuyingPressureAndCompactAsync(DateTime.UtcNow.AddDays(-365)));
    }

    private async Task RunAsync(string status, Func<Task> action)
    {
        try
        {
            txtStatus.Text = status;
            IsEnabled = false;
            await action();
            await RefreshAsync();
            txtStatus.Text = "Concluído.";
        }
        catch (Exception ex) { txtStatus.Text = $"A manutenção não foi concluída: {ex.Message}"; }
        finally { IsEnabled = true; }
    }

    private static string FormatBytes(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("F2", CultureInfo.CurrentCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("F1", CultureInfo.CurrentCulture) + " MB";

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
