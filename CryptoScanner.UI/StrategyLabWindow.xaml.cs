using System.Windows;
using CryptoScanner.Core.Contracts;
namespace CryptoScanner.UI;
public partial class StrategyLabWindow : Window
{
    private readonly IStrategyLabRepository _repository;
    private readonly CancellationTokenSource _closed=new();
    private bool _busy,_enabled;
    public StrategyLabWindow(IStrategyLabRepository repository)
    {
        InitializeComponent();_repository=repository;
        Loaded+=async(_,_)=>await LoadAsync();Closed+=(_,_)=>_closed.Cancel();
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _repository is not IStrategyLabExporter exporter) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar histórico completo do laboratório",
            Filter = "Relatório ZIP (*.zip)|*.zip",
            FileName = $"laboratorio-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        _busy = true;
        refresh.IsEnabled = pause.IsEnabled = export.IsEnabled = false;
        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            summary.Text = "Exportando todo o histórico…";
            await Task.Run(async () =>
            {
                await using (var stream = new System.IO.FileStream(temporary, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
                    await exporter.ExportAsync(stream, _closed.Token);
                _closed.Token.ThrowIfCancellationRequested();
                System.IO.File.Move(temporary, dialog.FileName, overwrite: true);
            }, _closed.Token);
            summary.Text = $"Relatório completo salvo em: {dialog.FileName}\nVocê pode enviar este ZIP para análise.";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { summary.Text = $"Falha ao exportar: {ex.Message}"; }
        finally
        {
            try { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); } catch (System.IO.IOException) { }
            _busy = false;
            refresh.IsEnabled = pause.IsEnabled = export.IsEnabled = true;
        }
    }
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private async void Pause_Click(object sender,RoutedEventArgs e)
    {
        if(_busy)return;
        await LoadAsync(toggle:true);
    }
    private async Task LoadAsync(bool toggle=false)
    {
        if(_busy)return;_busy=true;refresh.IsEnabled=false;pause.IsEnabled=false;export.IsEnabled=false;
        try
        {
            var report=await Task.Run(async()=>
            {
                if(toggle)await _repository.SetEnabledAsync(!_enabled,_closed.Token);
                return await _repository.ReportAsync(_closed.Token);
            },_closed.Token);
            if(_closed.IsCancellationRequested)return;
            _enabled=report.Enabled;pause.Content=_enabled?"Pausar novas entradas":"Retomar novas entradas";
            variants.ItemsSource=report.Variants;trades.ItemsSource=report.Trades;decisions.ItemsSource=report.Decisions;
            summary.Text=$"{report.Opportunities:N0} oportunidades registradas · {(_enabled?"Entradas ativas":"Entradas pausadas; posições continuam acompanhadas")}\nPatrimônio inclui posições abertas na última cotação. Amostras iniciais não definem uma estratégia vencedora. Passe o mouse sobre uma variante para ver sua alteração.";
        }
        catch(OperationCanceledException) when(_closed.IsCancellationRequested){}
        catch(Exception ex){summary.Text=$"Falha ao carregar o laboratório: {ex.Message}";}
        finally{_busy=false;refresh.IsEnabled=true;pause.IsEnabled=true;export.IsEnabled=true;}
    }
}
