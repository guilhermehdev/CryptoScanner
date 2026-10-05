using CryptoScanner.Core.Models;
using CryptoScanner.Application.Services;
using System.Windows;

namespace CryptoScanner.UI;

public partial class TechnicalSetupHistoryWindow : Window
{
    public TechnicalSetupHistoryWindow(IReadOnlyList<TechnicalSetupAlert> alerts)
    {
        InitializeComponent();
        dgSetups.ItemsSource = alerts;
        dgEvolution.ItemsSource = TechnicalSetupPerformanceAnalyzer.Build(alerts);
    }
}
