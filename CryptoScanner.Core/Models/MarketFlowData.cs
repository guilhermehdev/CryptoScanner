namespace CryptoScanner.Core.Models;

public sealed class MarketFlowData
{
    public IReadOnlyList<FlowCandle> PressureCandles { get; init; } = [];
    public IReadOnlyList<OpenInterestSample> OpenInterestHistory { get; init; } = [];
    public decimal TakerBuyRatio { get; init; }
    public decimal OpenInterestChange { get; init; }
    public decimal FundingRate { get; init; }
}
