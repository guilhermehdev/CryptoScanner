using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class ChartWindow : Window
{
    private readonly string _symbol;
    private readonly string _interval;

    public ChartWindow(string symbol, string interval)
    {
        InitializeComponent();
        _symbol = symbol;
        _interval = interval;
        Title = symbol;
        Loaded += ChartWindow_Loaded;
    }

    private async void ChartWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Navigate($"https://br.tradingview.com/chart/?symbol=BINANCE:{_symbol}&interval={_interval}&theme=dark");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o gráfico.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}