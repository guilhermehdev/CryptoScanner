namespace CryptoScanner.Core.Models.Analysis;

public sealed class RiskAnalysis
{
    public decimal Resistance { get; init; }
    public decimal Support { get; init; }
    public decimal ResistanceDistancePercent { get; init; }
    public decimal SupportDistancePercent { get; init; }
    public decimal RiskReward { get; init; }
}
