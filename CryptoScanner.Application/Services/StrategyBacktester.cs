using CryptoScanner.Application.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Utilities;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class StrategyBacktester
{
    private const int LookbackCandles = 300;

    private readonly IMarketDataService _marketData;
    private readonly AssetAnalyzer _assetAnalyzer;

    public StrategyBacktester(IMarketDataService marketData, AssetAnalyzer assetAnalyzer)
    {
        _marketData = marketData;
        _assetAnalyzer = assetAnalyzer;
    }

    public async Task<BacktestSummary> RunAsync(
     IReadOnlyList<string> symbols,
     DateTime startUtc,
     DateTime endUtc,
     ScanProfile profile,
     EligibilityThresholds? thresholds = null,
     Action<string, double>? onProgress = null,
     CancellationToken cancellationToken = default)
    {
        var intervalSpan = CandleIntervalHelper.ToTimeSpan(profile.CandleInterval);
        var fetchStart = startUtc - TimeSpan.FromTicks(intervalSpan.Ticks * LookbackCandles);

        onProgress?.Invoke("Buscando dados do BTC...", 0);
        var btcCandles = await _marketData.GetHistoricalCandlesAsync("BTCUSDT", profile.CandleInterval, fetchStart, endUtc, cancellationToken);

        var dailyFetchStart = startUtc - TimeSpan.FromDays(220);
        var btcDailyCandles = await _marketData.GetHistoricalCandlesAsync("BTCUSDT", "1d", dailyFetchStart, endUtc, cancellationToken);

        var allTrades = new List<BacktestTradeResult>();
        var diagnostics = new FilterDiagnostics();

        int totalSymbols = symbols.Count;
        int completedSymbols = 0;
        int index = 0;

        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            double baseProgress = totalSymbols > 0 ? completedSymbols * 100.0 / totalSymbols : 0;
            onProgress?.Invoke($"Testando {symbol} ({index}/{totalSymbols})...", baseProgress);

            List<Candle> candles;
            try
            {
                candles = await _marketData.GetHistoricalCandlesAsync(symbol, profile.CandleInterval, fetchStart, endUtc, cancellationToken);
            }
            catch
            {
                completedSymbols++;
                continue;
            }

            if (candles.Count < LookbackCandles)
            {
                completedSymbols++;
                continue;
            }

            int completedSoFarCapture = completedSymbols; // evita captura incorreta da variável mutável no closure

            var (trades, symbolDiagnostics) = await Task.Run(
                () => SimulateSymbol(symbol, candles, btcCandles, btcDailyCandles, startUtc, profile, thresholds,
                    (message, withinSymbolPercent) => {double overallPercent = totalSymbols > 0 ? (completedSoFarCapture + withinSymbolPercent / 100.0) * 100.0 / totalSymbols : 0;
                        onProgress?.Invoke(message, overallPercent);
                    }),
                cancellationToken);

            allTrades.AddRange(trades);
            MergeDiagnostics(diagnostics, symbolDiagnostics);
            completedSymbols++;
        }

        onProgress?.Invoke("Calculando resumo...", 100);
        return BuildSummary(allTrades, diagnostics);
    }

    private (List<BacktestTradeResult> Trades, FilterDiagnostics Diagnostics) SimulateSymbol(
     string symbol,
     List<Candle> candles,
     List<Candle> btcCandles,
     List<Candle> btcDailyCandles,
     DateTime startUtc,
     ScanProfile profile,
     EligibilityThresholds? thresholds,
     Action<string, double>? onProgress)
    {
        var trades = new List<BacktestTradeResult>();
        var diagnostics = new FilterDiagnostics();
        BacktestOpenPosition? openPosition = null;
        var lastSignalTimeByKey = new Dictionary<string, DateTime>();

        int startIndex = candles.FindIndex(c => c.OpenTime >= startUtc);
        if (startIndex < 0 || startIndex < LookbackCandles)
            startIndex = LookbackCandles;

        int totalToProcess = candles.Count - startIndex;
        const int progressReportInterval = 200;

        int skippedInsufficientData = 0;

        for (int i = startIndex; i < candles.Count; i++)
        {
            if ((i - startIndex) % progressReportInterval == 0)
            {
                int processed = i - startIndex;
                int percent = totalToProcess > 0 ? processed * 100 / totalToProcess : 0;
                onProgress?.Invoke($"Testando {symbol}: {percent}% ({processed}/{totalToProcess} candles)...", percent);
            }

            var currentCandle = candles[i];

            if (openPosition != null)
            {
                if (currentCandle.Low <= openPosition.StopLoss)
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.StopLoss, "SL"));
                    openPosition = null;
                }
                else if (currentCandle.High >= openPosition.TakeProfit)
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.TakeProfit, "TP"));
                    openPosition = null;
                }
                else if (currentCandle.OpenTime >= openPosition.EntryTime.AddHours(profile.EvaluationHours))
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, currentCandle.Close, "TIMEOUT"));
                    openPosition = null;
                }
            }

            if (openPosition != null)
                continue;

            var candlesSoFar = candles.GetRange(0, i + 1);
            var btcCandlesSoFar = btcCandles.Where(c => c.OpenTime <= currentCandle.OpenTime).ToList();
            var btcDailySoFar = btcDailyCandles.Where(c => c.OpenTime <= currentCandle.OpenTime).ToList();

            if (btcCandlesSoFar.Count < 60 || btcDailySoFar.Count < 200)
            {
                skippedInsufficientData++;
                continue;
            }

            decimal btcEma200 = EmaIndicator.Calculate(btcDailySoFar, 200)[^1] ?? 0;
            string marketRegime = MarketRegimeIndicator.Calculate(btcDailySoFar[^1].Close, btcEma200);

            var analysis = _assetAnalyzer.Analyze(symbol, candlesSoFar, btcCandlesSoFar, profile);
            var eligibility = EligibilityEvaluator.Evaluate(analysis, marketRegime, thresholds);

            diagnostics.TotalAnalyzed++;
            if (eligibility.FailedScore) diagnostics.FailedScore++;
            if (eligibility.FailedBreakout) diagnostics.FailedBreakout++;
            if (eligibility.FailedConsolidation) diagnostics.FailedConsolidation++;
            if (eligibility.FailedVolumeSpike) diagnostics.FailedVolumeSpike++;
            if (eligibility.FailedResistanceDistance) diagnostics.FailedResistanceDistance++;
            if (eligibility.FailedDirection) diagnostics.FailedDirection++;
            if (eligibility.FailedRiskReward) diagnostics.FailedRiskReward++;
            if (eligibility.FailedStopDistance) diagnostics.FailedStopDistance++;

            if (!eligibility.IsEligible)
                continue;

            string key = analysis.Signal;
            if (lastSignalTimeByKey.TryGetValue(key, out var lastTime) &&
                currentCandle.OpenTime < lastTime.AddDays(profile.DuplicateSignalWindowDays))
            {
                diagnostics.SkippedDuplicateToday++;
                continue;
            }

            lastSignalTimeByKey[key] = currentCandle.OpenTime;
            diagnostics.PassedAll++;

            openPosition = new BacktestOpenPosition
            {
                Symbol = symbol,
                EntryTime = currentCandle.OpenTime,
                EntryPrice = analysis.Trend.Close,
                TakeProfit = analysis.Risk.Resistance,
                StopLoss = analysis.Risk.Support,
                Signal = analysis.Signal,
                Score = analysis.OpportunityScore,
                ResistanceDistancePercent = analysis.Risk.ResistanceDistancePercent,
                SupportDistancePercent = analysis.Risk.SupportDistancePercent,
                RiskRewardAtEntry = analysis.Risk.RiskReward
            };
        }

        if (skippedInsufficientData > 0 && diagnostics.TotalAnalyzed == 0)
            System.Diagnostics.Debug.WriteLine($"[Backtest] {symbol}: todas as {skippedInsufficientData} janelas puladas por falta de candles de BTC suficientes.");

        return (trades, diagnostics);
    }

    private static void MergeDiagnostics(FilterDiagnostics target, FilterDiagnostics source)
    {
        target.TotalAnalyzed += source.TotalAnalyzed;
        target.PassedAll += source.PassedAll;
        target.FailedScore += source.FailedScore;
        target.FailedBreakout += source.FailedBreakout;
        target.FailedConsolidation += source.FailedConsolidation;
        target.FailedVolumeSpike += source.FailedVolumeSpike;
        target.FailedResistanceDistance += source.FailedResistanceDistance;
        target.FailedDirection += source.FailedDirection;
        target.FailedRiskReward += source.FailedRiskReward;
        target.SkippedDuplicateToday += source.SkippedDuplicateToday;
        target.FailedStopDistance += source.FailedStopDistance;
    }

    private static BacktestTradeResult CloseTrade(BacktestOpenPosition position, DateTime exitTime, decimal exitPrice, string reason)
    {
        decimal outcomePercent = ((exitPrice - position.EntryPrice) / position.EntryPrice) * 100m;

        return new BacktestTradeResult
        {
            Symbol = position.Symbol,
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            ExitTime = exitTime,
            ExitPrice = exitPrice,
            OutcomePercent = outcomePercent,
            ExitReason = reason,
            Signal = position.Signal,
            Score = position.Score,
            ResistanceDistancePercent = position.ResistanceDistancePercent,
            SupportDistancePercent = position.SupportDistancePercent,
            RiskRewardAtEntry = position.RiskRewardAtEntry
        };
    }

    private static BacktestSummary BuildSummary(List<BacktestTradeResult> trades, FilterDiagnostics diagnostics)
    {
        var ordered = trades.OrderBy(t => t.ExitTime).ToList();

        int total = ordered.Count;
        int wins = ordered.Count(t => t.OutcomePercent > 0);
        double winRate = total > 0 ? wins * 100.0 / total : 0;

        decimal totalReturn = ordered.Sum(t => t.OutcomePercent);

        decimal grossProfit = ordered.Where(t => t.OutcomePercent > 0).Sum(t => t.OutcomePercent);
        decimal grossLoss = Math.Abs(ordered.Where(t => t.OutcomePercent < 0).Sum(t => t.OutcomePercent));
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? decimal.MaxValue : 0);

        decimal runningTotal = 0;
        decimal peak = 0;
        decimal maxDrawdown = 0;

        foreach (var trade in ordered)
        {
            runningTotal += trade.OutcomePercent;
            if (runningTotal > peak)
                peak = runningTotal;

            decimal drawdown = peak - runningTotal;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        return new BacktestSummary
        {
            TotalTrades = total,
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            MaxDrawdownPercent = maxDrawdown,
            ProfitFactor = profitFactor,
            Trades = ordered,
            Diagnostics = diagnostics
        };
    }

    private sealed class BacktestOpenPosition
    {
        public required string Symbol { get; init; }
        public required DateTime EntryTime { get; init; }
        public required decimal EntryPrice { get; init; }
        public required decimal TakeProfit { get; init; }
        public required decimal StopLoss { get; init; }
        public required string Signal { get; init; }
        public required decimal Score { get; init; }
        public required decimal ResistanceDistancePercent { get; init; }
        public required decimal SupportDistancePercent { get; init; }
        public required decimal RiskRewardAtEntry { get; init; }
    }
}