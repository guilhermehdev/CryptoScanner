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
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await LoadAsync();
    private async void Pause_Click(object sender,RoutedEventArgs e)
    {
        if(_busy)return;
        await LoadAsync(toggle:true);
    }
    private async Task LoadAsync(bool toggle=false)
    {
        if(_busy)return;_busy=true;refresh.IsEnabled=false;pause.IsEnabled=false;
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
        finally{_busy=false;refresh.IsEnabled=true;pause.IsEnabled=true;}
    }
}
