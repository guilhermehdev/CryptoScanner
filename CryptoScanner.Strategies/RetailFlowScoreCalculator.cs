using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

public static class RetailFlowScoreCalculator
{
    public static decimal Calculate(MarketFlowData flow)
    {
        decimal takerScore = Math.Clamp(flow.TakerBuyRatio * 100m, 0m, 100m);
        decimal oiScore = Math.Clamp(50m + flow.OpenInterestChange * 10m, 0m, 100m);
        decimal fundingScore = Math.Clamp(50m + flow.FundingRate * 50000m, 0m, 100m);
        return Math.Round(takerScore * 0.50m + oiScore * 0.30m + fundingScore * 0.20m, 2);
    }
}