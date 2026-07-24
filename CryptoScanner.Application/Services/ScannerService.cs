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

        var btcCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1d", 300, cancellationToken);
        decimal btcEma200 = EmaIndicator.Calculate(btcCandles, 200)[^1] ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcCandles[^1].Close, btcEma200);

        var pendingSignals = await _signals.GetPendingSignalsAsync(cancellationToken);
        var symbols = (await _marketData.GetUsdtSymbolsAsync(cancellationToken))
            .Take(ScannerSettings.MaxCoins)
            .ToList();

        using var throttle = new SemaphoreSlim(MaxConcurrentAnalyses);
        var analyses = symbols.Select(symbol => AnalyzeSymbolAsync(symbol, throttle, cancellationToken));
        var analysesResult = (await Task.WhenAll(analyses))
            .OfType<AssetAnalysis>()
            .OrderByDescending(asset => asset.OpportunityScore)
            .Take(30)
            .ToList();

        await EvaluatePendingSignalsAsync(pendingSignals, cancellationToken);
        await PersistEligibleSignalsAsync(analysesResult, marketRegime, cancellationToken);

        var history = await _signals.GetSignalsAsync(cancellationToken);
        return new ScannerRunResult
        {
            MarketRegime = marketRegime,
            Ranking = analysesResult.Select(AssetScoreFactory.Create).ToList(),
            History = history,
            WinRate = await _signals.GetWinRateAsync(cancellationToken),
            AverageReturn = await _signals.GetAverageReturnAsync(cancellationToken)
        };
    }

    private async Task<AssetAnalysis?> AnalyzeSymbolAsync(string symbol, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            var candles = await _marketData.GetCandlesAsync(symbol, "1h", 300, cancellationToken);
            return _assetAnalyzer.Analyze(symbol, candles);
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
            if (DateTime.UtcNow < signal.Timestamp.AddHours(ScannerSettings.EvaluationHours))
                continue;

            decimal currentPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
            decimal outcomePercent = ((currentPrice - signal.Price) / signal.Price) * 100m;
            await _signals.UpdateSignalResultAsync(signal.Id, currentPrice, outcomePercent, cancellationToken);
        }
    }

    private async Task PersistEligibleSignalsAsync(
        IReadOnlyList<AssetAnalysis> ranking,
        string marketRegime,
        CancellationToken cancellationToken)
    {
        foreach (var asset in ranking)
        {
            decimal opportunity = marketRegime switch
            {
                "BEAR" => asset.OpportunityScore - 15m,
                "SIDEWAYS" => asset.OpportunityScore - 8m,
                _ => asset.OpportunityScore
            };

            if (opportunity < ScannerSettings.BuyOpportunityScore ||
                !asset.Setup.IsBreakout ||
                !asset.Setup.IsConsolidating ||
                asset.Volume.Spike < ScannerSettings.MinVolumeSpike ||
                asset.Risk.ResistanceDistancePercent < ScannerSettings.MinResistanceDistance ||
                asset.Trend.Direction != "ALTA" ||
                asset.Risk.RiskReward < ScannerSettings.MinRiskReward)
            {
                continue;
            }

            if (await _signals.SignalExistsTodayAsync(asset.Symbol, asset.Signal, cancellationToken))
                continue;

            await _signals.InsertSignalAsync(asset.Symbol, asset.Trend.Close, asset.OpportunityScore, asset.Signal, cancellationToken);
        }
    }
}
