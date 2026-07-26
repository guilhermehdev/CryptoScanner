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

    public async Task<ScannerRunResult> RunAsync(ScanProfile profile, CancellationToken cancellationToken = default)
    {
        await _signals.InitializeAsync(cancellationToken);

        var btcDailyCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1d", 300, cancellationToken);
        decimal btcEma200 = EmaIndicator.Calculate(btcDailyCandles, 200)[^1] ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcDailyCandles[^1].Close, btcEma200);

        var btcCandles = await _marketData.GetCandlesAsync("BTCUSDT", profile.CandleInterval, 300, cancellationToken);

        var pendingSignals = await _signals.GetPendingSignalsAsync(cancellationToken);
        var symbols = (await _marketData.GetUsdtSymbolsAsync(cancellationToken))
            .Take(ScannerSettings.MaxCoins)
            .ToList();

        using var throttle = new SemaphoreSlim(MaxConcurrentAnalyses);
        var analyses = symbols.Select(symbol => AnalyzeSymbolAsync(symbol, btcCandles, profile, throttle, cancellationToken));
        var analysesResult = (await Task.WhenAll(analyses))
            .OfType<AssetAnalysis>()
            .OrderByDescending(asset => asset.OpportunityScore)
            .Take(30)
            .ToList();

        await EvaluatePendingSignalsAsync(pendingSignals, profile, cancellationToken);
        var diagnostics = await PersistEligibleSignalsAsync(analysesResult, marketRegime, profile, cancellationToken);

        var history = await _signals.GetSignalsAsync(cancellationToken);
        return new ScannerRunResult
        {
            MarketRegime = marketRegime,
            Ranking = analysesResult.Select(asset => AssetScoreFactory.Create(asset, marketRegime)).ToList(),
            History = history,
            WinRate = await _signals.GetWinRateAsync(cancellationToken),
            AverageReturn = await _signals.GetAverageReturnAsync(cancellationToken),
            Diagnostics = diagnostics
        };
    }

    private async Task<AssetAnalysis?> AnalyzeSymbolAsync(string symbol, List<Candle> btcCandles, ScanProfile profile, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            var candles = await _marketData.GetCandlesAsync(symbol, profile.CandleInterval, 300, cancellationToken);
            return _assetAnalyzer.Analyze(symbol, candles, btcCandles, profile);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task EvaluatePendingSignalsAsync(IReadOnlyList<SignalHistory> pendingSignals, ScanProfile profile, CancellationToken cancellationToken)
    {
        foreach (var signal in pendingSignals)
        {
            int evaluationHours = signal.Profile == ScanProfile.Intraday.Name
                ? ScanProfile.Intraday.EvaluationHours
                : signal.Profile == ScanProfile.Swing.Name
                    ? ScanProfile.Swing.EvaluationHours
                    : profile.EvaluationHours; // fallback pra sinais legados sem Profile gravado

            if (signal.TakeProfit <= 0 || signal.StopLoss <= 0)
            {
                if (DateTime.UtcNow < signal.Timestamp.AddHours(evaluationHours))
                    continue;

                decimal legacyPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
                decimal legacyOutcome = ((legacyPrice - signal.Price) / signal.Price) * 100m;
                await _signals.UpdateSignalResultAsync(signal.Id, legacyPrice, legacyOutcome, "TIMEOUT", cancellationToken);
                continue;
            }

            List<Candle> candles;
            try
            {
                candles = await _marketData.GetCandlesAsync(signal.Symbol, "1h", evaluationHours + 24, cancellationToken);
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

            bool timeoutReached = DateTime.UtcNow >= signal.Timestamp.AddHours(evaluationHours);

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
        ScanProfile profile,
        CancellationToken cancellationToken)
    {
        var diagnostics = new FilterDiagnostics { TotalAnalyzed = ranking.Count };

        bool defensiveMode = marketRegime != "BULL";

        foreach (var asset in ranking)
        {
            decimal opportunity = marketRegime switch
            {
                "BEAR" => asset.OpportunityScore - ScannerSettings.BearRegimePenalty,
                "SIDEWAYS" => asset.OpportunityScore - ScannerSettings.SidewaysRegimePenalty,
                _ => asset.OpportunityScore
            };

            bool failedScore = opportunity < ScannerSettings.BuyOpportunityScore;

            bool passesBreakout = defensiveMode
                ? (asset.Setup.IsBreakout
                    || asset.Setup.IsShortTermBreakout
                    || asset.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent)
                : asset.Setup.IsBreakout;
            bool failedBreakout = !passesBreakout;

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

            if (await _signals.SignalExistsWithinWindowAsync(asset.Symbol, asset.Signal, profile.DuplicateSignalWindowDays, cancellationToken))
            {
                diagnostics.SkippedDuplicateToday++;
                continue;
            }

            diagnostics.PassedAll++;

            var snapshot = new SignalSnapshot
            {
                Symbol = asset.Symbol,
                Price = asset.Trend.Close,
                Score = asset.OpportunityScore,
                Signal = asset.Signal,
                PreviousScore = asset.PreviousScore,
                TakeProfit = asset.Risk.Resistance,
                StopLoss = asset.Risk.Support,
                Profile = profile.Name,
                MarketRegime = marketRegime,

                Rsi = asset.Trend.Rsi,
                Adx = asset.Trend.Adx,
                AtrPercent = asset.Trend.AtrPercent,
                EmaDistanceAtr = asset.Setup.EmaDistanceAtr,
                SwingUsageAtr = asset.Setup.SwingUsageAtr,
                VolumeSpike = asset.Volume.Spike,
                VolumeImbalance = asset.Volume.Imbalance,
                RelativeStrength = asset.Setup.RelativeStrength,
                RiskReward = asset.Risk.RiskReward,

                TrendScore = asset.Trend.Score,
                StructureScore = asset.Structure.Score,
                VolumeScore = asset.Volume.Score,
                CandleScore = asset.Candle.Score,
                SetupScore = asset.Setup.Score,
                MomentumScore = asset.Trend.MomentumScore,
                VolatilityScore = asset.Trend.VolatilityScore,
                TrendStrengthScore = asset.Trend.TrendStrengthScore,

                PatternName = asset.Candle.PatternName,
                SmartMoneyLabel = asset.Structure.SmartMoneyLabel,
                BreakoutSource = asset.Setup.IsBreakout ? "Clássico" :
                                  asset.Setup.IsShortTermBreakout ? "Curto Prazo" :
                                  asset.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent ? "Força Rel." : "",
                IsBullTrap = asset.Structure.IsBullTrap,
                IsBearTrap = asset.Structure.IsBearTrap
            };

            await _signals.InsertSignalAsync(snapshot, cancellationToken);
        }

        return diagnostics;
    }
}
    
