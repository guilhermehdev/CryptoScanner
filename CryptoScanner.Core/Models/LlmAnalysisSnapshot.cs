namespace CryptoScanner.Core.Models;

// Immutable scanner state captured immediately before one LLM request.
public sealed class LlmAnalysisSnapshot
{
    public const string CurrentVersion = "llm-snapshot-v1";

    public required string Version { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string Symbol { get; init; }
    public required string Profile { get; init; }
    public required string CandleInterval { get; init; }
    public required string ScannerSignal { get; init; }
    public required string Direction { get; init; }
    public required string Trend { get; init; }
    public required string MarketRegime { get; init; }
    public required bool IsEligible { get; init; }
    public required bool CanRecommendTrade { get; init; }
    public required string EligibilityDetails { get; init; }
    public required string VolumeStatus { get; init; }
    public required decimal AnalysisPrice { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required decimal Score { get; init; }
    public required decimal Rsi { get; init; }
    public required decimal Adx { get; init; }
    public required decimal AtrPercent { get; init; }
    public required decimal VolumeSpike { get; init; }
    public required decimal? BuyingPressureScore { get; init; }
    public required decimal RelativeStrength { get; init; }
    public required decimal RiskReward { get; init; }
    public required decimal Support { get; init; }
    public required decimal Resistance { get; init; }
    public required decimal? ExpectedEntry { get; init; }
    public required decimal? ExpectedStop { get; init; }
    public required decimal? ExpectedTp1 { get; init; }
    public required decimal? ExpectedTp2 { get; init; }
    public required bool IsBullTrap { get; init; }
    public required bool IsBearTrap { get; init; }
    public required string PatternName { get; init; }
    public required string StrategyRules { get; init; }
}
