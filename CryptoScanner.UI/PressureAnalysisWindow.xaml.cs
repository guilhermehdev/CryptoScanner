using CryptoScanner.Core.Models;
using CryptoScanner.Infrastructure.Sqlite;
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

    private async Task RefreshAsync()
    {
        if (_loading) return;
        if (fromDate.SelectedDate is not DateTime from || toDate.SelectedDate is not DateTime to || from.Date>to.Date)
        { summary.Text="Selecione um período válido de coleta."; return; }
        string asset=symbol.Text.Trim().ToUpperInvariant();
        if (asset.Length>0 && !asset.EndsWith("USDT")) asset+="USDT";
        int minutes=int.Parse(((ComboBoxItem)horizon.SelectedItem).Tag.ToString()!);
        long start=new DateTimeOffset(DateTime.SpecifyKind(from.Date,DateTimeKind.Local)).ToUnixTimeMilliseconds();
        long finish=new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1),DateTimeKind.Local)).ToUnixTimeMilliseconds();
        _loading=true; filters.IsEnabled=false;
        bands.ItemsSource=null; history.ItemsSource=null; historyLabel.Text="Histórico";
        summary.Text="Carregando leituras e resultados…";
        try
        {
            var filter=new PressureAnalysisFilter(start,finish,asset,minutes,BuyingPressureSnapshot.FormulaVersion);
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
}
