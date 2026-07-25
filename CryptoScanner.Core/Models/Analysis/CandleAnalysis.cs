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
    public bool IsDoji { get; init; }
    public bool IsHammer { get; init; }
    public bool IsShootingStar { get; init; }
    public bool IsBullishMarubozu { get; init; }
    public bool IsBearishMarubozu { get; init; }
    public bool IsBullishEngulfing { get; init; }
    public bool IsBearishEngulfing { get; init; }
    public string PatternName { get; init; } = "";
}