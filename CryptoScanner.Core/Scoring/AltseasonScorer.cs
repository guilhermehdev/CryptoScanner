using CryptoScanner.Core.Models.Altseason;

namespace CryptoScanner.Core.Scoring;

public static class AltseasonScorer
{
    private const decimal BtcDominanceWeight = 0.20m;
    private const decimal EthBtcWeight = 0.15m;
    private const decimal Total3Weight = 0.15m;
    private const decimal StablecoinWeight = 0.10m;
    private const decimal BreadthWeight = 0.15m;
    private const decimal BtcTrendWeight = 0.10m;
    private const decimal AltVolumeWeight = 0.10m;
    private const decimal DefiWeight = 0.05m;

    public static AltseasonScore Calculate(AltseasonSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var indicators = new List<AltseasonIndicatorScore>
        {
            Score("BTC Dominance", ScoreBtcDominance(snapshot.BtcDominance, snapshot.Previous?.BtcDominance), BtcDominanceWeight),
            Score("ETH/BTC", ScoreDirection(snapshot.EthBtc, snapshot.Previous?.EthBtc), EthBtcWeight),
            Score("TOTAL3", ScoreDirection(snapshot.Total3MarketCap, snapshot.Previous?.Total3MarketCap), Total3Weight),
            Score("Stablecoins", ScoreDirection(snapshot.StablecoinMarketCap, snapshot.Previous?.StablecoinMarketCap), StablecoinWeight),
            Score("Altcoin Breadth", Clamp(snapshot.AltcoinBreadthPercent), BreadthWeight),
            Score("BTC Trend", ScoreBtcTrend(snapshot), BtcTrendWeight),
            Score("Altcoin Volume", ScoreChange(snapshot.AltcoinVolumeChangePercent), AltVolumeWeight),
            Score("DeFi TVL", ScoreChange(snapshot.DefiTvlChangePercent), DefiWeight)
        };

        var score = indicators.Sum(x => x.Contribution);
        var previousScore = snapshot.Previous is null ? score : CalculateWithoutPrevious(snapshot.Previous);

        return new AltseasonScore
        {
            Score = Math.Round(score, 2),
            PreviousScore = Math.Round(previousScore, 2),
            MarketRegimeScore = ScoreBtcTrend(snapshot),
            Indicators = indicators
        };
    }

    private static decimal CalculateWithoutPrevious(AltseasonSnapshot snapshot)
        => Calculate(snapshot with { Previous = null }).Score;

    private static AltseasonIndicatorScore Score(string name, decimal value, decimal weight)
        => new() { Name = name, Score = Clamp(value), Weight = weight };

    private static decimal ScoreBtcDominance(decimal current, decimal? previous)
    {
        if (current <= 0) return 0;
        if (!previous.HasValue || previous.Value <= 0) return 50m;

        var change = (current - previous.Value) / previous.Value * 100m;
        return change switch
        {
            <= -3m => 100m,
            <= -1.5m => 85m,
            <= -0.5m => 70m,
            < 0m => 60m,
            <= 0.5m => 45m,
            <= 1.5m => 30m,
            <= 3m => 15m,
            _ => 0m
        };
    }

    private static decimal ScoreDirection(decimal current, decimal? previous)
    {
        if (current <= 0 || !previous.HasValue || previous.Value <= 0) return 50m;
        var change = (current - previous.Value) / previous.Value * 100m;
        return change switch
        {
            >= 5m => 100m,
            >= 2m => 85m,
            >= 0.75m => 70m,
            > 0m => 60m,
            >= -0.75m => 45m,
            >= -2m => 30m,
            >= -5m => 15m,
            _ => 0m
        };
    }

    private static decimal ScoreChange(decimal changePercent)
        => changePercent switch
        {
            >= 15m => 100m,
            >= 8m => 85m,
            >= 3m => 70m,
            > 0m => 60m,
            >= -3m => 45m,
            >= -8m => 30m,
            >= -15m => 15m,
            _ => 0m
        };

    private static decimal ScoreBtcTrend(AltseasonSnapshot snapshot)
    {
        if (snapshot.BtcPrice <= 0 || snapshot.Previous is null || snapshot.Previous.BtcPrice <= 0)
            return 50m;

        var change = (snapshot.BtcPrice - snapshot.Previous.BtcPrice) / snapshot.Previous.BtcPrice * 100m;
        return change switch
        {
            >= 5m => 100m,
            >= 2m => 85m,
            >= 0.5m => 70m,
            > -0.5m => 55m,
            >= -2m => 40m,
            >= -5m => 20m,
            _ => 0m
        };
    }

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
}
