using System.Windows;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using MessageBox = System.Windows.MessageBox;
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
    private async void ApplyParameters_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var pressureText = new[] { txtPressure1.Text, txtPressure2.Text, txtPressure3.Text, txtPressure4.Text, txtPressure5.Text };
        var stopText = new[] { txtStop1.Text, txtStop2.Text, txtStop3.Text, txtStop4.Text, txtStop5.Text };
        if (pressureText.Any(v => !decimal.TryParse(v, out _)) || stopText.Any(v => !decimal.TryParse(v, out var stop) || stop <= 0))
        {
            MessageBox.Show("Informe pressão numérica e escala de stop maior que zero para as cinco variantes.", "Laboratório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _busy = true;
            var current = (await _repository.GetParametersAsync(_closed.Token)).OrderBy(p => p.Id).ToArray();
            var baseParameters = current.Length == 5 ? current : LabParameters.Initial;
            var updated = baseParameters.Select((p, i) => p with
            {
                MinimumPressure = decimal.Parse(pressureText[i]),
                StopScale = decimal.Parse(stopText[i])
            }).ToArray();
            await _repository.UpdateParametersAsync(updated, _closed.Token);
            await LoadParametersAsync();
            summary.Text = "Parâmetros aplicados às novas oportunidades. Trades já abertos foram preservados.";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { summary.Text = $"Falha ao salvar parâmetros: {ex.Message}"; }
        finally { _busy = false; }
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || cmbLabPreset.SelectedIndex != 1)
            return;

        try
        {
            _busy = true;
            await _repository.UpdateParametersAsync(LabParameters.Initial, _closed.Token);
            await LoadParametersAsync();
            summary.Text = "Preset simultâneo aplicado: V1 Validado, V2 Exploração e V3 Diagnóstico. V4 e V5 permanecem mutações do Validado.";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { summary.Text = $"Falha ao aplicar preset: {ex.Message}"; }
        finally { _busy = false; }
    }
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
            await LoadParametersAsync();
            shadowTrades.ItemsSource=report.ShadowTrades;variants.ItemsSource=report.Variants;trades.ItemsSource=report.Trades;decisions.ItemsSource=report.Decisions;
            summary.Text=$"{report.Opportunities:N0} oportunidades registradas · {report.ShadowCount:N0} testes sem vaga (fora das carteiras) · {(_enabled?"Entradas ativas":"Entradas pausadas; posições continuam acompanhadas")}\nPatrimônio inclui posições abertas na última cotação. Amostras iniciais não definem uma estratégia vencedora. Passe o mouse sobre uma variante para ver sua alteração.";
        }
        catch(OperationCanceledException) when(_closed.IsCancellationRequested){}
        catch(Exception ex){summary.Text=$"Falha ao carregar o laboratório: {ex.Message}";}
        finally{_busy=false;refresh.IsEnabled=true;pause.IsEnabled=true;export.IsEnabled=true;}
    }

    private async Task LoadParametersAsync()
    {
        var parameters = (await _repository.GetParametersAsync(_closed.Token)).OrderBy(p => p.Id).ToArray();
        if (parameters.Length != 5) parameters = LabParameters.Initial.ToArray();
        var pressure = new[] { txtPressure1, txtPressure2, txtPressure3, txtPressure4, txtPressure5 };
        var stop = new[] { txtStop1, txtStop2, txtStop3, txtStop4, txtStop5 };
        for (int i = 0; i < 5; i++)
        {
            pressure[i].Text = parameters[i].MinimumPressure.ToString("G");
            stop[i].Text = parameters[i].StopScale.ToString("G");
        }
    }
}
