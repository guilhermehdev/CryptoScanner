namespace CryptoScanner.Core.Models.Analysis;

public sealed class CandleAnalysis
{
    public int Score { get; init; }
    public decimal BullPower { get; init; }
    public decimal BearPower { get; init; }
    public decimal BodyRatio { get; init; }
    public decimal UpperWickRatio { get; init; }
    public decimal LowerWickRatio { get; init; }
    public bool IsStrongBullish { get; init; }
    public bool IsStrongBearish { get; init; }
    public bool HasBuyerRejection { get; init; }
    public bool HasSellerRejection { get; init; }
    public decimal RejectionScore { get; init; }
}
