using CryptoScanner.Application.Services;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class AltseasonWindow : Window
{
    private readonly AltseasonLiveDataService _service = new();

    public AltseasonWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        btnRefresh.IsEnabled = false;
        try
        {
            var result = await _service.GetAsync();
            txtScore.Text = $"{result.Score.Score:F0}/100";
            txtState.Text = result.Score.State;
            txtAction.Text = result.Score.Action;
            txtMarket.Text = $"BTC ${result.Snapshot.BtcPrice:N0}  |  BTC.D {result.Snapshot.BtcDominance:F1}%  |  ETH/BTC {result.Snapshot.EthBtc:G6}  |  TOTAL3 ${result.Snapshot.Total3MarketCap / 1_000_000_000m:N1}B  |  Breadth {result.Snapshot.AltcoinBreadthPercent:F0}%";
            dgIndicators.ItemsSource = result.Score.Indicators;
            txtUpdated.Text = $"Atualizado {result.Snapshot.TimestampUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível atualizar o monitor de altseason.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { btnRefresh.IsEnabled = true; }
    }
}
