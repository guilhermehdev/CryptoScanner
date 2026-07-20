using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CryptoScanner.UI;

using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Indicators.Indicators;
using CryptoScanner.Strategies;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender,RoutedEventArgs e)
    {
        BinanceExchangeService service = new();

        var symbols =
     await service.GetUsdtSymbolsAsync();
        symbols = symbols.Take(50).ToList();

        List<AssetScore> ranking = new();

        foreach (string symbol in symbols)
        {
            var candles =
                await service.GetCandlesAsync(
                    symbol,
                    "1h",
                    300);

            var ema21 =
                EmaIndicator.Calculate(candles, 21);

            var ema50 =
                EmaIndicator.Calculate(candles, 50);

            var ema200 =
                EmaIndicator.Calculate(candles, 200);

            decimal close = candles.Last().Close;

            decimal e21 = ema21.Last() ?? 0;
            decimal e50 = ema50.Last() ?? 0;
            decimal e200 = ema200.Last() ?? 0;

            int score =
                TrendScorer.Calculate(
                    close,
                    e21,
                    e50,
                    e200);

            ranking.Add(
                new AssetScore
                {
                    Symbol = symbol,
                    Score = score,
                    Close = close,
                    Ema21 = e21,
                    Ema50 = e50,
                    Ema200 = e200
                });
        }

        ranking = ranking
            .OrderByDescending(x => x.Score)
            .ToList();

        string msg = "";

        MessageBox.Show($"Moedas encontradas: {symbols.Count}");

        foreach (var item in ranking)
        {
            msg +=
                $"{item.Symbol} - Score: {item.Score}\n";
        }

        dgRanking.ItemsSource = ranking;

    }


}

