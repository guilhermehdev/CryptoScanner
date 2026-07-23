using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Scoring;
using CryptoScanner.Core.Services;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Indicators;
using CryptoScanner.Indicators.Indicators;
using CryptoScanner.Strategies;
using CryptoScanner.UI.Services;
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
using System.Windows.Threading;

namespace CryptoScanner.UI;

public partial class MainWindow : Window
{
    private DispatcherTimer _timer = new();
    public decimal OpportunityScore { get; set; }
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        _timer.Interval = TimeSpan.FromMinutes(3);
        _timer.Tick += Timer_Tick;
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        _timer.Stop();

        try
        {
            await RunScannerAsync();
        }
        finally
        {
            _timer.Start();
        }
    }

    private async Task RunScannerAsync()
    {
        const int EvaluationHours = 24;
        var db = new SignalDatabase();
        BinanceExchangeService service = new();
        List<AssetScore> ranking = new();
        await db.InitializeAsync();

        var btcCandles = await service.GetCandlesAsync("BTCUSDT", "1d", 300);
        decimal btcClose = btcCandles.Last().Close;
        decimal btcEma200 = EmaIndicator.Calculate(btcCandles, 200).Last() ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcClose, btcEma200);
        var pendingSignals = await db.GetPendingSignalsAsync();
        var symbols = await service.GetUsdtSymbolsAsync();      
        symbols = symbols.Take(200).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = symbols.Select(symbol => AnalyzeSymbolAsync(service, symbol));
        var results = await Task.WhenAll(tasks); 
        ranking = results.Where(x => x != null).Cast<AssetScore>().OrderByDescending(x => x.FinalScore).ToList();
        ranking = ranking.OrderByDescending(x => x.OpportunityScore).ToList();

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

            if (marketRegime == "BEAR")
            {
                if (asset.FinalScore < 80)
                    continue;
            }
            else
            {
                if (asset.FinalScore < 60)
                    continue;
            }

            if (!asset.IsBreakout)
                continue;

            if (!asset.IsConsolidating)
                continue;

            if (asset.VolumeSpike < 1.3m)
                continue;

            if (asset.ResistanceDistance < 8)
                continue;

            if (asset.TrendDirection != "ALTA")
                continue;

            if (asset.RiskReward < 3)
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

        txtWinRate.Text = $"Win Rate: {winRate:F1}%";
        txtAvgReturn.Text = $"Retorno Médio: {avgReturn:F2}%";
        txtPending.Text = $"Pendentes: {historySignals.Count(x => !x.Evaluated)}";
        txtEvaluated.Text = $"Avaliados: {historySignals.Count(x => x.Evaluated)}";

        sw.Stop();

        Title = $"Scanner [{marketRegime}] | WinRate: {winRate:F1}% | Avg: {avgReturn:F2}%";
    }

    private async void MainWindow_Loaded(object sender,RoutedEventArgs e)
    {
        await RunScannerAsync();
        _timer.Start();
    }                                                                               

    private async Task<AssetScore?> AnalyzeSymbolAsync(BinanceExchangeService service,string symbol)
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
            decimal rompimento = BreakoutIndicator.GetResistance(candles);
            decimal atr = AtrIndicator.Calculate(candles);
            decimal atrPercent = 0;          
            decimal close = candles.Last().Close;
            if (close > 0)
            {
                atrPercent = (atr / close) * 100;
            }
            decimal e21 = ema21.Last() ?? 0;
            decimal e50 = ema50.Last() ?? 0;
            decimal e200 = ema200.Last() ?? 0;
            int score = TrendScorer.Calculate(close, e21, e50, e200);
            var task1H = CalculateTimeframeScore(service, symbol,"1h");
            var task4H = CalculateTimeframeScore(service, symbol,"4h");
            var task1D = CalculateTimeframeScore(service, symbol,"1d");
            await Task.WhenAll(task1H,task4H,task1D);
            int score1H = task1H.Result;
            int score4H = task4H.Result;
            int score1D = task1D.Result;
            var structure = MarketStructureAnalyzer.Calculate(candles);
            int marketStructureScore = structure.Score;
            int volatilityScore = VolatilityScorer.Calculate(atrPercent);
            decimal adx = AdxIndicator.Calculate(candles);
            int momentumScore = MomentumScorer.Calculate(lastRsi);
            var volume = VolumeAnalyzer.Calculate(candles);
            // int volumeScore = VolumeScorer.Calculate(rvol, volumeSpike, breakout);
            int volumeScore = volume.Score;
            
            int trendStrengthScore = TrendStrengthScorer.Calculate(close, e21, e50, e200);
            bool consolidating = ConsolidationIndicator.IsConsolidating(candles);
            decimal resistance = SupportResistanceIndicator.GetResistance(candles);
            decimal support = SupportResistanceIndicator.GetSupport(candles);
            decimal resistanceDistance = ((resistance - close) / close) * 100m;
            decimal supportDistance = ((close - support) / close) * 100m;
            decimal riskReward = 0;
            if (supportDistance > 0)
                riskReward = resistanceDistance / supportDistance;
            decimal finalScore = (marketStructureScore * 0.30m + momentumScore * 0.20m + volumeScore * 0.20m + volatilityScore * 0.10m + trendStrengthScore * 0.20m);
            if (breakout)
                finalScore += 15;
            if (consolidating)
                finalScore += 10;          
            decimal scoreVariation = ScoreTracker.GetVariation(symbol, finalScore);
            decimal rejectionScore = RejectionScore.Calculate(candles);
            if (rejectionScore > 0.60m)
                finalScore -= 15;
            else if (rejectionScore > 0.40m)
                finalScore -= 8;
            else if (rejectionScore > 0.25m)
                finalScore -= 4;

            string trendDirection = structure.Uptrend ? "ALTA" : structure.Downtrend ? "BAIXA" : "LATERAL";    
            


            AssetScore asset = new()
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
                Resistance = rompimento,
                MarketStructureScore = marketStructureScore,
                MomentumScore = momentumScore,
                VolumeScore = volumeScore,
                VolatilityScore = volatilityScore,
                Adx = adx,
                VolumeSpike = volume.ClimaxVolume ? 3m : 1m,
                TrendStrengthScore = trendStrengthScore,
                IsConsolidating = consolidating,
                TrendDirection = trendDirection,
                ScoreVariation = scoreVariation,
                ResistanceDistance = resistanceDistance,
                SupportDistance = supportDistance,               
                RiskReward = riskReward,
                RejectionScore = rejectionScore,
                StrongUptrend = structure.StrongUptrend,
                StrongDowntrend = structure.StrongDowntrend,
                BreakOfStructure = structure.BreakOfStructure,
                ChangeOfCharacter = structure.ChangeOfCharacter,
                BuyingVolume = volume.BuyingVolume,
                SellingVolume = volume.SellingVolume,
                VolumeImbalance = volume.VolumeImbalance,
                ClimaxVolume = volume.ClimaxVolume,
                Absorption = volume.Absorption,
                Distribution = volume.Distribution,
              
            };
            asset.OpportunityScore = OpportunityScoreCalculator.Calculate(asset);

            asset.IsEliteSetup =
            asset.OpportunityScore >= 75 &&
            asset.TrendDirection == "ALTA" &&
            asset.RiskReward >= 2.5m &&
            asset.RejectionScore <= 0.40m;

            return asset;
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
        var candles = await service.GetCandlesAsync(symbol,timeframe,300);
        var ema21 = EmaIndicator.Calculate(candles, 21);
        var ema50 = EmaIndicator.Calculate(candles, 50);
        var ema200 = EmaIndicator.Calculate(candles, 200);
        var rsi =  RsiIndicator.Calculate(candles);
        decimal close = candles.Last().Close;
        decimal e21 = ema21.Last() ?? 0;
        decimal e50 = ema50.Last() ?? 0;
        decimal e200 = ema200.Last() ?? 0;
        decimal lastRsi = rsi.Last() ?? 0;
        decimal rvol = RelativeVolumeIndicator.Calculate(candles);
        decimal atr = AtrIndicator.Calculate(candles);
        decimal atrPercent = close > 0 ? (atr / close) * 100 : 0;
        bool breakout = BreakoutIndicator.IsBullishBreakout(candles);

        return TrendScorer.Calculate(close,e21,e50,e200);
    }    

    private async void BtnBacktest_Click(object sender, RoutedEventArgs e)
    {
        BinanceExchangeService service = new();

        var candles =
            await service.GetCandlesAsync(
                "BTCUSDT",
                "1h",
                1000);

        BacktestEngine engine = new();

        var result =
            engine.Run(candles);

        MessageBox.Show(
            $"""
        Trades: {result.Trades}

        WinRate: {result.WinRate:F2}%

        Lucro: {result.NetProfit:F2}%
        """);
    }

    private async void btAtualizar_Click(object sender, RoutedEventArgs e)
    {
        await RunScannerAsync();
    }
}

