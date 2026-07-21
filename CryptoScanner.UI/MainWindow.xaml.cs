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
using CryptoScanner.Indicators;
using CryptoScanner.Indicators.Indicators;
using CryptoScanner.Strategies;
using CryptoScanner.UI.Services;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender,RoutedEventArgs e)
    {
        const int EvaluationHours = 0;
        var db = new SignalDatabase();
        BinanceExchangeService service = new();
        List<AssetScore> ranking = new();

        await db.InitializeAsync();

        var pendingSignals = await db.GetPendingSignalsAsync();  
        var symbols = await service.GetUsdtSymbolsAsync();
        symbols = symbols.Take(50).ToList();      
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = symbols.Select(symbol =>AnalyzeSymbolAsync(service, symbol));
        var results = await Task.WhenAll(tasks);
        var historyService = new SignalHistoryService();
        var history = await historyService.LoadAsync();
        string msg = "";

        ranking = results
            .Where(x => x != null)
            .Cast<AssetScore>()
            .OrderByDescending(x => x.FinalScore)
            .ToList();

        ranking = ranking
            .OrderByDescending(x => x.FinalScore)
            .ToList();

        MessageBox.Show(
    $"Pendentes: {pendingSignals.Count}");

        foreach (var signal in pendingSignals)
        {           
            if (DateTime.UtcNow < signal.Timestamp.AddHours(EvaluationHours))
            {              
                continue;
            }

            decimal currentPrice =
                await service.GetCurrentPriceAsync(
                    signal.Symbol);

            decimal outcomePercent =
                ((currentPrice - signal.Price)
                    / signal.Price) * 100m;

            await db.UpdateSignalResultAsync(signal.Id, currentPrice, outcomePercent);
         
        }

        foreach (var asset in ranking)
        {
            if (asset.FinalScore < 75)
                continue;

            history.Add(
                new SignalHistory
                {
                    Timestamp = DateTime.UtcNow,
                    Symbol = asset.Symbol,
                    Price = asset.Close,
                    FinalScore = asset.FinalScore,
                    Signal = asset.Signal
                });
        }

        await historyService.SaveAsync(history);  
        //MessageBox.Show($"Moedas encontradas: {symbols.Count}");

        foreach (var item in ranking)
        {
            msg += $"{item.Symbol} - FinalScore: {item.FinalScore:F1}\n";
        }

        foreach (var asset in ranking)
        {
            if (asset.FinalScore < 60)
                continue;

            if (await db.SignalExistsTodayAsync(asset.Symbol, asset.Signal))
                continue;

            await db.InsertSignalAsync(
                asset.Symbol,
                asset.Close,
                asset.FinalScore,
                asset.Signal);
        }

        var historySignals = await db.GetSignalsAsync();

        dgHistory.ItemsSource = historySignals;
        dgRanking.ItemsSource = ranking;

        double winRate = await db.GetWinRateAsync();
        double avgReturn = await db.GetAverageReturnAsync();

        txtWinRate.Text =
            $"Win Rate: {winRate:F1}%";

        txtAvgReturn.Text =
            $"Retorno Médio: {avgReturn:F2}%";

        txtPending.Text =
            $"Pendentes: {historySignals.Count(x => !x.Evaluated)}";

        txtEvaluated.Text =
            $"Avaliados: {historySignals.Count(x => x.Evaluated)}";

        sw.Stop();

        Title = $"Scanner | WinRate: {winRate:F1}% | Avg: {avgReturn:F2}%";
    }                                                                               

    private async Task<AssetScore?> AnalyzeSymbolAsync(
    BinanceExchangeService service,
    string symbol)
    {
        try
        {
            var candles = await service.GetCandlesAsync(symbol,"1h",300);

            var ema21 = EmaIndicator.Calculate(candles, 21);

            var ema50 = EmaIndicator.Calculate(candles, 50);

            var ema200 = EmaIndicator.Calculate(candles, 200);

            var rsi = RsiIndicator.Calculate(candles);

            decimal lastRsi = rsi.Last() ?? 0;

            decimal rvol = RelativeVolumeIndicator.Calculate(candles);

            bool breakout = BreakoutIndicator.IsBullishBreakout(candles);

            decimal resistance = BreakoutIndicator.GetResistance(candles);

            decimal atr = AtrIndicator.Calculate(candles);

            decimal atrPercent = 0;          

            decimal close = candles.Last().Close;
            if (close > 0)
            {
                atrPercent =
                    (atr / close) * 100;
            }

            decimal e21 = ema21.Last() ?? 0;

            decimal e50 = ema50.Last() ?? 0;

            decimal e200 = ema200.Last() ?? 0;

            int score =
     TrendScorer.Calculate(
         close,
         e21,
         e50,
         e200,
         lastRsi,
         rvol,
         atrPercent,
         breakout);

            var task1H =
           CalculateTimeframeScore(
               service,
               symbol,
               "1h");

            var task4H =
                CalculateTimeframeScore(
                    service,
                    symbol,
                    "4h");

            var task1D =
                CalculateTimeframeScore(
                    service,
                    symbol,
                    "1d");

            await Task.WhenAll(
                task1H,
                task4H,
                task1D);

            int score1H = task1H.Result;
            int score4H = task4H.Result;
            int score1D = task1D.Result;

            decimal finalScore =
                (score1H * 0.2m) +
                (score4H * 0.3m) +
                (score1D * 0.5m);

            if (breakout)
                finalScore += 15;


            return new AssetScore
            {
                Symbol = symbol,
                FinalScore = finalScore,
                Close = close,
                Ema21 = e21,
                Ema50 = e50,
                Ema200 = e200,
                Rsi = lastRsi,
                RelativeVolume = rvol,
                Atr = atr,
                AtrPercent = atrPercent,
                Score1H = score1H,
                Score4H = score4H,
                Score1D = score1D,                
                IsBreakout = breakout,
                Resistance = resistance,
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<int> CalculateTimeframeScore(
    BinanceExchangeService service,
    string symbol,
    string timeframe)
    {
        var candles =
            await service.GetCandlesAsync(
                symbol,
                timeframe,
                300);

        var ema21 =
            EmaIndicator.Calculate(candles, 21);

        var ema50 =
            EmaIndicator.Calculate(candles, 50);

        var ema200 =
            EmaIndicator.Calculate(candles, 200);

        var rsi =
            RsiIndicator.Calculate(candles);

        decimal close = candles.Last().Close;

        decimal e21 = ema21.Last() ?? 0;
        decimal e50 = ema50.Last() ?? 0;
        decimal e200 = ema200.Last() ?? 0;

        decimal lastRsi = rsi.Last() ?? 0;

        decimal rvol =
            RelativeVolumeIndicator.Calculate(candles);

        decimal atr =
            AtrIndicator.Calculate(candles);

        decimal atrPercent =
            close > 0
                ? (atr / close) * 100
                : 0;

        bool breakout = BreakoutIndicator.IsBullishBreakout(candles);

        return TrendScorer.Calculate(
            close,
            e21,
            e50,
            e200,
            lastRsi,
            rvol,
            atrPercent,
            breakout);
    }

  
}

