using CryptoScanner.Core.Models;
using CryptoScanner.Infrastructure.Sqlite;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace CryptoScanner.UI;

public partial class PressureAnalysisWindow : Window
{
    private readonly SqlitePressureAnalysisRepository _repository;
    private readonly CancellationTokenSource _closed = new();
    private bool _loading;

    public PressureAnalysisWindow(string databasePath)
    {
        InitializeComponent();
        _repository = new(databasePath);
        fromDate.SelectedDate=DateTime.Today.AddDays(-6);
        toDate.SelectedDate=DateTime.Today;
        Loaded += async (_,_) => await RefreshAsync();
        Closed += (_,_) => _closed.Cancel();
    }

    private async void Refresh_Click(object sender,RoutedEventArgs e) => await RefreshAsync();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || !TryBuildFilter(out var filter)) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar leituras da pressão compradora",
            Filter = "CSV UTF-8|*.csv",
            FileName = $"pressao_compradora_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        _loading = true; filters.IsEnabled = export.IsEnabled = false;
        try
        {
            summary.Text = "Exportando todas as leituras do filtro…";
            long exported = await Task.Run(async () =>
            {
                await using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
                return await _repository.ExportCsvAsync(filter, stream, _closed.Token);
            }, _closed.Token);
            summary.Text = $"{exported:N0} leituras exportadas para análise.";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { summary.Text = $"Não foi possível exportar a análise: {ex.Message}"; }
        finally { _loading = false; filters.IsEnabled = export.IsEnabled = true; }
    }

    private async Task RefreshAsync()
    {
        if (_loading) return;
        if (!TryBuildFilter(out var filter)) return;
        _loading=true; filters.IsEnabled=false;
        bands.ItemsSource=null; history.ItemsSource=null; historyLabel.Text="Histórico";
        summary.Text="Carregando leituras e resultados…";
        try
        {
            // SQLite operations may complete synchronously; keep large reports off the UI thread.
            var report=await Task.Run(() => _repository.LoadAsync(filter,_closed.Token),_closed.Token);
            if (_closed.IsCancellationRequested) return;
            bands.ItemsSource=report.Bands; history.ItemsSource=report.History;
            long evaluated=report.Bands.Sum(b=>b.Evaluated);
            summary.Text=report.TotalReadings==0
                ? "Nenhuma leitura neste filtro. O histórico é acumulado enquanto o scanner está rodando."
                : $"{report.TotalReadings:N0} leituras · {report.Unavailable:N0} sem dados · {evaluated:N0} avaliadas · " +
                  $"{report.Bands.Sum(b=>b.Pending):N0} aguardando prazo · {report.Bands.Sum(b=>b.Overdue):N0} aguardando recuperação";
            summary.Text+=$" · Fórmula: {BuyingPressureSnapshot.FormulaVersion}";
            historyLabel.Text=$"Histórico · {report.History.Count:N0} leituras mais recentes de {report.TotalReadings:N0} (máximo {SqlitePressureAnalysisRepository.HistoryLimit})";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { summary.Text=$"Não foi possível carregar a análise: {ex.Message}"; }
        finally { _loading=false; filters.IsEnabled=true; }
    }

    private bool TryBuildFilter(out PressureAnalysisFilter filter)
    {
        filter = default!;
        if (fromDate.SelectedDate is not DateTime from || toDate.SelectedDate is not DateTime to || from.Date > to.Date)
        {
            summary.Text = "Selecione um período válido de coleta.";
            return false;
        }
        string asset = symbol.Text.Trim().ToUpperInvariant();
        if (asset.Length > 0 && !asset.EndsWith("USDT")) asset += "USDT";
        int minutes = int.Parse(((ComboBoxItem)horizon.SelectedItem).Tag.ToString()!);
        long start = new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        long finish = new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        filter = new PressureAnalysisFilter(start, finish, asset, minutes, BuyingPressureSnapshot.FormulaVersion);
        return true;
    }
}
