using CryptoScanner.Application.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class ScannerService
{
    private const int MaxConcurrentAnalyses = 8;

    private readonly IMarketDataService _marketData;
    private readonly ISignalRepository _signals;
    private readonly AssetAnalyzer _assetAnalyzer;

    public ScannerService(
        IMarketDataService marketData,
        ISignalRepository signals,
        AssetAnalyzer assetAnalyzer)
    {
        _marketData = marketData;
        _signals = signals;
        _assetAnalyzer = assetAnalyzer;
    }

    public async Task<ScannerRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _signals.InitializeAsync(cancellationToken);

        var btcDailyCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1d", 300, cancellationToken);
        decimal btcEma200 = EmaIndicator.Calculate(btcDailyCandles, 200)[^1] ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcDailyCandles[^1].Close, btcEma200);

        // Mesmo timeframe dos candles por-símbolo (1h), para comparação justa de força relativa.
        var btcHourlyCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1h", 300, cancellationToken);

        var pendingSignals = await _signals.GetPendingSignalsAsync(cancellationToken);
        var symbols = (await _marketData.GetUsdtSymbolsAsync(cancellationToken))
            .Take(ScannerSettings.MaxCoins)
            .ToList();

        using var throttle = new SemaphoreSlim(MaxConcurrentAnalyses);
        var analyses = symbols.Select(symbol => AnalyzeSymbolAsync(symbol, btcHourlyCandles, throttle, cancellationToken));
        var analysesResult = (await Task.WhenAll(analyses))
            .OfType<AssetAnalysis>()
            .OrderByDescending(asset => asset.OpportunityScore)
            .Take(30)
            .ToList();

        await EvaluatePendingSignalsAsync(pendingSignals, cancellationToken);
        var diagnostics = await PersistEligibleSignalsAsync(analysesResult, marketRegime, cancellationToken);

        var history = await _signals.GetSignalsAsync(cancellationToken);
        return new ScannerRunResult
        {
            MarketRegime = marketRegime,
            Ranking = analysesResult.Select(AssetScoreFactory.Create).ToList(),
            History = history,
            WinRate = await _signals.GetWinRateAsync(cancellationToken),
            AverageReturn = await _signals.GetAverageReturnAsync(cancellationToken),
            Diagnostics = diagnostics
        };
    }

    private async Task<AssetAnalysis?> AnalyzeSymbolAsync(string symbol, List<Candle> btcCandles, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            var candles = await _marketData.GetCandlesAsync(symbol, "1h", 300, cancellationToken);
            return _assetAnalyzer.Analyze(symbol, candles, btcCandles);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A future logging implementation should record symbol, status code and exception.
            return null;
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task EvaluatePendingSignalsAsync(IReadOnlyList<SignalHistory> pendingSignals, CancellationToken cancellationToken)
    {
        foreach (var signal in pendingSignals)
        {
            if (signal.TakeProfit <= 0 || signal.StopLoss <= 0)
            {
                if (DateTime.UtcNow < signal.Timestamp.AddHours(ScannerSettings.EvaluationHours))
                    continue;

                decimal legacyPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
                decimal legacyOutcome = ((legacyPrice - signal.Price) / signal.Price) * 100m;
                await _signals.UpdateSignalResultAsync(signal.Id, legacyPrice, legacyOutcome, "TIMEOUT", cancellationToken);
                continue;
            }

            List<Candle> candles;
            try
            {
                candles = await _marketData.GetCandlesAsync(signal.Symbol, "1h", ScannerSettings.EvaluationHours + 24, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            var relevant = candles.Where(c => c.OpenTime >= signal.Timestamp).OrderBy(c => c.OpenTime).ToList();

            bool hitTakeProfit = false;
            bool hitStopLoss = false;
            decimal exitPrice = signal.Price;

            foreach (var candle in relevant)
            {
                if (candle.Low <= signal.StopLoss)
                {
                    hitStopLoss = true;
                    exitPrice = signal.StopLoss;
                    break;
                }

                if (candle.High >= signal.TakeProfit)
                {
                    hitTakeProfit = true;
                    exitPrice = signal.TakeProfit;
                    break;
                }
            }

            bool timeoutReached = DateTime.UtcNow >= signal.Timestamp.AddHours(ScannerSettings.EvaluationHours);

            if (hitTakeProfit || hitStopLoss)
            {
                decimal outcomePercent = ((exitPrice - signal.Price) / signal.Price) * 100m;
                string reason = hitTakeProfit ? "TP" : "SL";
                await _signals.UpdateSignalResultAsync(signal.Id, exitPrice, outcomePercent, reason, cancellationToken);
            }
            else if (timeoutReached)
            {
                decimal currentPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
                decimal outcomePercent = ((currentPrice - signal.Price) / signal.Price) * 100m;
                await _signals.UpdateSignalResultAsync(signal.Id, currentPrice, outcomePercent, "TIMEOUT", cancellationToken);
            }
        }
    }

    private async Task<FilterDiagnostics> PersistEligibleSignalsAsync(
        IReadOnlyList<AssetAnalysis> ranking,
        string marketRegime,
        CancellationToken cancellationToken)
    {
        var diagnostics = new FilterDiagnostics { TotalAnalyzed = ranking.Count };

        bool defensiveMode = marketRegime != "BULL";

        foreach (var asset in ranking)
        {
            decimal opportunity = marketRegime switch
            {
                "BEAR" => asset.OpportunityScore - ScannerSettings.BearRegimePenalty,
                "LATERAL" => asset.OpportunityScore - ScannerSettings.SidewaysRegimePenalty,
                _ => asset.OpportunityScore
            };

            bool failedScore = opportunity < ScannerSettings.BuyOpportunityScore;

            // Em modo defensivo, qualquer um dos três sinais (breakout clássico, breakout de
            // curto prazo, ou força relativa vs. BTC) já é suficiente.
            bool passesBreakout = defensiveMode
                ? (asset.Setup.IsBreakout
                    || asset.Setup.IsShortTermBreakout
                    || asset.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent)
                : asset.Setup.IsBreakout;
            bool failedBreakout = !passesBreakout;

            // Consolidação deixa de ser exigida em modo defensivo — o setup alvo aqui é
            // força relativa/momentum de curto prazo, não necessariamente range apertado.
            bool failedConsolidation = defensiveMode ? false : !asset.Setup.IsConsolidating;

            decimal volumeSpikeThreshold = defensiveMode
                ? ScannerSettings.DefensiveMinVolumeSpike
                : ScannerSettings.MinVolumeSpike;
            bool failedVolumeSpike = asset.Volume.Spike < volumeSpikeThreshold;

            bool failedResistanceDistance = asset.Risk.ResistanceDistancePercent < ScannerSettings.MinResistanceDistance;
            bool failedDirection = asset.Trend.Direction != "ALTA";
            bool failedRiskReward = asset.Risk.RiskReward < ScannerSettings.MinRiskReward;

            if (failedScore) diagnostics.FailedScore++;
            if (failedBreakout) diagnostics.FailedBreakout++;
            if (failedConsolidation) diagnostics.FailedConsolidation++;
            if (failedVolumeSpike) diagnostics.FailedVolumeSpike++;
            if (failedResistanceDistance) diagnostics.FailedResistanceDistance++;
            if (failedDirection) diagnostics.FailedDirection++;
            if (failedRiskReward) diagnostics.FailedRiskReward++;

            bool eligible = !failedScore && !failedBreakout && !failedConsolidation &&
                             !failedVolumeSpike && !failedResistanceDistance &&
                             !failedDirection && !failedRiskReward;

            if (!eligible)
                continue;

            if (await _signals.SignalExistsTodayAsync(asset.Symbol, asset.Signal, cancellationToken))
            {
                diagnostics.SkippedDuplicateToday++;
                continue;
            }

            diagnostics.PassedAll++;

            await _signals.InsertSignalAsync(
                asset.Symbol,
                asset.Trend.Close,
                asset.OpportunityScore,
                asset.Signal,
                asset.PreviousScore,
                asset.Risk.Resistance,
                asset.Risk.Support,
                cancellationToken);
        }

        return diagnostics;
    }
}