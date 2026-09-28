using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Mede o que ocorreu depois de um gatilho sem aplicar stop, alvo ou custo. Isso evita
/// confundir a qualidade do padrão com as regras de execução de uma estratégia.
/// </summary>
public static class CandidateOutcomeResearch
{
    public static BacktestResearchCandidate? TryCreate(
        IReadOnlyList<Candle> candles,
        int entryIndex,
        TimeSpan interval,
        AssetAnalysis analysis,
        string marketRegime,
        bool passedAllFilters,
        IEnumerable<string> failures)
    {
        if (entryIndex < 0 || entryIndex >= candles.Count)
            return null;

        var entryCandle = candles[entryIndex];
        if (entryCandle.Open <= 0)
            return null;

        DateTime entryTime = entryCandle.OpenTime;
        int after6Hours = FindFirstClosedAtOrAfter(candles, entryIndex, entryTime.AddHours(6), interval);
        int after24Hours = FindFirstClosedAtOrAfter(candles, entryIndex, entryTime.AddHours(24), interval);
        if (after6Hours < 0 || after24Hours < 0)
            return null;

        decimal entry = entryCandle.Open;
        decimal DirectionalReturn(decimal price) => analysis.Direction == TradeDirection.Long
            ? (price - entry) / entry * 100m
            : (entry - price) / entry * 100m;

        decimal favorable = 0;
        decimal adverse = 0;
        for (int i = entryIndex; i <= after24Hours; i++)
        {
            var candle = candles[i];
            decimal candleFavorable = analysis.Direction == TradeDirection.Long
                ? DirectionalReturn(candle.High)
                : DirectionalReturn(candle.Low);
            decimal candleAdverse = analysis.Direction == TradeDirection.Long
                ? -DirectionalReturn(candle.Low)
                : -DirectionalReturn(candle.High);
            favorable = Math.Max(favorable, candleFavorable);
            adverse = Math.Max(adverse, candleAdverse);
        }

        return new BacktestResearchCandidate
        {
            Symbol = analysis.Symbol,
            Direction = analysis.Direction,
            Strategy = analysis.EntryStrategy,
            DecisionTime = entryTime - interval,
            EntryTime = entryTime,
            EntryPrice = entry,
            MarketRegime = marketRegime,
            PassedAllFilters = passedAllFilters,
            Failures = string.Join("|", failures),
            Score = analysis.OpportunityScore,
            Rsi = analysis.Trend.Rsi,
            Adx = analysis.Trend.Adx,
            AtrPercent = analysis.Trend.AtrPercent,
            VolumeSpike = analysis.Volume.Spike,
            VolumeImbalance = analysis.Volume.Imbalance,
            CloseAfter6Hours = candles[after6Hours].OpenTime + interval,
            ReturnAfter6HoursPercent = DirectionalReturn(candles[after6Hours].Close),
            CloseAfter24Hours = candles[after24Hours].OpenTime + interval,
            ReturnAfter24HoursPercent = DirectionalReturn(candles[after24Hours].Close),
            MaximumFavorable24HoursPercent = favorable,
            MaximumAdverse24HoursPercent = adverse
        };
    }

    private static int FindFirstClosedAtOrAfter(IReadOnlyList<Candle> candles, int startIndex, DateTime target, TimeSpan interval)
    {
        for (int i = startIndex; i < candles.Count; i++)
            if (candles[i].OpenTime + interval >= target)
                return i;
        return -1;
    }
}
