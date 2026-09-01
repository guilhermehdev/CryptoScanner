namespace CryptoScanner.Core.Models;

public sealed class MarketFlowData
{
    public decimal TakerBuyRatio { get; init; }
    public decimal OpenInterestChange { get; init; }
    public decimal FundingRate { get; init; }
}