using CryptoScanner.Application.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
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

    private const int MaxConcurrentSymbols = 6;

    public async Task<BacktestSummary> RunAsync(
     IReadOnlyList<string> symbols,
     DateTime startUtc,
     DateTime endUtc,
     ScanProfile profile,
     EligibilityThresholds? thresholds = null,
     RiskCalculationMode riskMode = RiskCalculationMode.SwingBased,
     int? evaluationHoursOverride = null,
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
        var skippedSymbols = new List<string>();

        var tradesLock = new object();
        var diagnosticsLock = new object();
        var progressLock = new object();

        int totalSymbols = symbols.Count;
        int completedSymbols = 0;

        using var throttle = new SemaphoreSlim(MaxConcurrentSymbols);

        var tasks = symbols.Select(async symbol =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<Candle> candles;
                try
                {
                    candles = await _marketData.GetHistoricalCandlesAsync(symbol, profile.CandleInterval, fetchStart, endUtc, cancellationToken);
                }
                catch (Exception ex)
                {
                    lock (tradesLock) { skippedSymbols.Add($"{symbol} (erro ao buscar dados: {ex.Message})"); }
                    return;
                }
                finally
                {
                    // Pausa por moeda individual, mesmo em paralelo — mitiga rate limit da Binance
                    // sem exigir que tudo rode em sequência única.
                    await Task.Delay(200, cancellationToken);
                }

                if (candles.Count < LookbackCandles)
                {
                    lock (tradesLock) { skippedSymbols.Add($"{symbol} (histórico insuficiente: {candles.Count} candles)"); }
                    return;
                }

                var (trades, symbolDiagnostics) = await Task.Run(
                    () => SimulateSymbol(symbol, candles, btcCandles, btcDailyCandles, startUtc, profile, thresholds, riskMode, evaluationHoursOverride,
                        (message, _) => { }),
                    cancellationToken);

                lock (tradesLock) { allTrades.AddRange(trades); }
                lock (diagnosticsLock) { MergeDiagnostics(diagnostics, symbolDiagnostics); }
            }
            finally
            {
                int done;
                lock (progressLock) { done = ++completedSymbols; }

                double overallPercent = totalSymbols > 0 ? done * 100.0 / totalSymbols : 0;
                onProgress?.Invoke($"Concluídas {done}/{totalSymbols} moedas...", overallPercent);

                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        onProgress?.Invoke("Calculando resumo...", 100);
        return BuildSummary(allTrades, diagnostics, skippedSymbols);
    }

    private (List<BacktestTradeResult> Trades, FilterDiagnostics Diagnostics) SimulateSymbol(
        string symbol,
        List<Candle> candles,
        List<Candle> btcCandles,
        List<Candle> btcDailyCandles,
        DateTime startUtc,
        ScanProfile profile,
        EligibilityThresholds? thresholds,
        RiskCalculationMode riskMode,
        int? evaluationHoursOverride,
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
        int effectiveEvaluationHours = evaluationHoursOverride ?? profile.EvaluationHours;

        // Ponteiros que só avançam — como as três listas estão em ordem cronológica,
        // dá pra manter a janela dos últimos 300 candles de cada uma sem reescanear
        // do início a cada iteração (o que tornava o teste quadrático em vez de linear).
        int btcIndex = 0;
        int btcDailyIndex = 0;

        for (int i = startIndex; i < candles.Count; i++)
        {
            if ((i - startIndex) % progressReportInterval == 0)
            {
                int processed = i - startIndex;
                int percent = totalToProcess > 0 ? processed * 100 / totalToProcess : 0;
                onProgress?.Invoke($"Testando {symbol}: {percent}% ({processed}/{totalToProcess} candles)...", percent);
            }

            var currentCandle = candles[i];
            bool justClosed = false;

            if (openPosition != null)
            {
                if (currentCandle.Low <= openPosition.StopLoss)
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.StopLoss, "SL"));
                    openPosition = null;
                    justClosed = true;
                }
                else if (currentCandle.High >= openPosition.TakeProfit)
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.TakeProfit, "TP"));
                    openPosition = null;
                    justClosed = true;
                }
                else if (currentCandle.OpenTime >= openPosition.EntryTime.AddHours(effectiveEvaluationHours))
                {
                    trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, currentCandle.Close, "TIMEOUT"));
                    openPosition = null;
                    justClosed = true;
                }
            }

            if (openPosition != null || justClosed)
                continue;

            // Janela deslizante dos últimos até 300 candles do próprio ativo — mesmo lookback
            // que o scanner ao vivo usa (nunca vê mais que 300 candles de contexto real).
            int assetWindowStart = Math.Max(0, i + 1 - LookbackCandles);
            var candlesSoFar = candles.GetRange(assetWindowStart, i + 1 - assetWindowStart);

            // Avança os ponteiros do BTC até o instante atual, sem reescanear do início —
            // seguro porque candles[] e btcCandles[]/btcDailyCandles[] estão em ordem crescente de tempo.
            while (btcIndex < btcCandles.Count && btcCandles[btcIndex].OpenTime <= currentCandle.OpenTime)
                btcIndex++;

            while (btcDailyIndex < btcDailyCandles.Count && btcDailyCandles[btcDailyIndex].OpenTime <= currentCandle.OpenTime)
                btcDailyIndex++;

            int btcWindowStart = Math.Max(0, btcIndex - LookbackCandles);
            var btcCandlesSoFar = btcCandles.GetRange(btcWindowStart, btcIndex - btcWindowStart);

            int btcDailyWindowStart = Math.Max(0, btcDailyIndex - LookbackCandles);
            var btcDailySoFar = btcDailyCandles.GetRange(btcDailyWindowStart, btcDailyIndex - btcDailyWindowStart);

            if (btcCandlesSoFar.Count < 60 || btcDailySoFar.Count < 200)
            {
                skippedInsufficientData++;
                continue;
            }

            decimal btcEma200 = EmaIndicator.Calculate(btcDailySoFar, 200)[^1] ?? 0;
            string marketRegime = MarketRegimeIndicator.Calculate(btcDailySoFar[^1].Close, btcEma200);

            var analysis = _assetAnalyzer.Analyze(symbol, candlesSoFar, btcCandlesSoFar, profile, riskMode);

            if (thresholds?.EnableBollingerScoring == true)
            {
                var (bbMiddle, bbUpper, bbLower, bbWidth) = BollingerBandsIndicator.Calculate(candlesSoFar);

                var scoringContext = new ScoringContext
                {
                    Candles = candlesSoFar,
                    Trend = analysis.Trend,
                    Volume = analysis.Volume,
                    Structure = analysis.Structure,
                    Risk = analysis.Risk,
                    BollingerMiddle = bbMiddle,
                    BollingerUpper = bbUpper,
                    BollingerLower = bbLower,
                    BollingerBandWidth = bbWidth,
                    AtrPercentSeries = new List<decimal?>(),
                    AdxSlope = null,
                    CandleRangePercentSeries = new List<decimal?>()
                };

                var scoringEngine = new ScoringEngine(new List<IScoringRule> { new BandWidthPercentileRule() });
                var (adjustment, _) = scoringEngine.Evaluate(scoringContext);

                analysis.OpportunityScore += adjustment;
            }

            if (thresholds?.EnableVolatilityScoringPhaseB == true)
            {
                var (bbMiddle, bbUpper, bbLower, bbWidth) = BollingerBandsIndicator.Calculate(candlesSoFar);

                var atrAbsoluteSeries = AtrSeriesCalculator.Calculate(candlesSoFar);
                var atrPercentSeries = atrAbsoluteSeries
                    .Select((v, i) => v.HasValue && candlesSoFar[i].Close != 0
                        ? (decimal?)(v.Value / candlesSoFar[i].Close * 100m)
                        : null)
                    .ToList();

                // ADX só precisa de 2 pontos (agora e N candles atrás) pra medir inclinação —
                // chama o indicador existente (que só retorna o valor mais recente) só duas
                // vezes, não recalcula uma série inteira candle a candle.
                const int adxSlopeLookback = 5;
                decimal? adxSlope = null;
                if (candlesSoFar.Count > adxSlopeLookback + 15) // margem de segurança pro período do ADX
                {
                    decimal currentAdx = AdxIndicator.Calculate(candlesSoFar);
                    var pastCandles = candlesSoFar.GetRange(0, candlesSoFar.Count - adxSlopeLookback);
                    decimal pastAdx = AdxIndicator.Calculate(pastCandles);
                    adxSlope = currentAdx - pastAdx;
                }

                var candleRangePercentSeries = candlesSoFar
                    .Select(c => c.Close != 0 ? (decimal?)((c.High - c.Low) / c.Close * 100m) : null)
                    .ToList();

                var phaseBContext = new ScoringContext
                {
                    Candles = candlesSoFar,
                    Trend = analysis.Trend,
                    Volume = analysis.Volume,
                    Structure = analysis.Structure,
                    Risk = analysis.Risk,
                    BollingerMiddle = bbMiddle,
                    BollingerUpper = bbUpper,
                    BollingerLower = bbLower,
                    BollingerBandWidth = bbWidth,
                    AtrPercentSeries = atrPercentSeries,
                    AdxSlope = adxSlope,
                    CandleRangePercentSeries = candleRangePercentSeries
                };

                var phaseBEngine = new ScoringEngine(new List<IScoringRule>
                {
                    new AtrLevelRule(),
                    new AtrSlopeRule(),
                    new BandExpansionRule(),
                    new CandleRangeRule(),
                    new StructureDistanceRule(),
                    new LiquidityRule()
                });

                var (phaseBAdjustment, _) = phaseBEngine.Evaluate(phaseBContext);
                analysis.OpportunityScore += phaseBAdjustment;
            }

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
            if (eligibility.FailedRiskRewardTooHigh) diagnostics.FailedRiskRewardTooHigh++;

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

        // Se os dados acabaram com uma posição ainda aberta (nunca bateu TP, SL nem timeout),
        // fecha ela a mercado no último candle disponível — sem isso, o trade some silenciosamente
        // do resultado, mesmo que estivesse com lucro no momento em que o teste terminou.
        if (openPosition != null)
        {
            var lastCandle = candles[^1];
            trades.Add(CloseTrade(openPosition, lastCandle.OpenTime, lastCandle.Close, "EOT"));
        }

        if (skippedInsufficientData > 0 && diagnostics.TotalAnalyzed == 0)
            System.Diagnostics.Debug.WriteLine($"[Backtest] {symbol}: todas as {skippedInsufficientData} janelas puladas por falta de candles de BTC suficientes.");

        return (trades, diagnostics);
    }


    public static void MergeDiagnostics(FilterDiagnostics target, FilterDiagnostics source)
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
        target.FailedRiskRewardTooHigh += source.FailedRiskRewardTooHigh;
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

    public static BacktestSummary BuildSummary(List<BacktestTradeResult> trades, FilterDiagnostics diagnostics, List<string> skippedSymbols)
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

        // Win Rate de equilíbrio: dado o RR médio real de entrada, qual seria o
        // percentual mínimo de acerto pra não ganhar nem perder dinheiro.
        decimal avgRiskReward = total > 0 ? ordered.Average(t => t.RiskRewardAtEntry) : 0;
        double breakEvenWinRate = avgRiskReward > 0 ? 100.0 / (1 + (double)avgRiskReward) : 0;
        double edge = winRate - breakEvenWinRate;

        return new BacktestSummary
        {
            TotalTrades = total,
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            MaxDrawdownPercent = maxDrawdown,
            ProfitFactor = profitFactor,
            Trades = ordered,
            Diagnostics = diagnostics,
            SkippedSymbols = skippedSymbols,
            AvgRiskRewardAtEntry = avgRiskReward,
            BreakEvenWinRate = breakEvenWinRate,
            Edge = edge
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