using CryptoScanner.Core.Models;
using System.Windows;

namespace CryptoScanner.UI;

public partial class TechnicalSetupHistoryWindow : Window
{
    public TechnicalSetupHistoryWindow(IReadOnlyList<TechnicalSetupAlert> alerts)
    {
        InitializeComponent();
        dgSetups.ItemsSource = alerts;
    }
}
