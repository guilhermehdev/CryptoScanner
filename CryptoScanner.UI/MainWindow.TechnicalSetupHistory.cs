using System.Windows;

namespace CryptoScanner.UI;

public partial class MainWindow
{
    private async void BtnTechnicalSetupHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var alerts = await _scanner.GetTechnicalSetupAlertsAsync();
            new TechnicalSetupHistoryWindow(alerts).Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Não foi possível carregar o histórico de setups: {ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
