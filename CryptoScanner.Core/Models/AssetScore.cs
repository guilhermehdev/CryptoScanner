namespace CryptoScanner.Core.Models;

// Projection used exclusively by the ranking UI.
public sealed class AssetScore
{
    public string Symbol { get; init; } = "";
    public decimal Close { get; init; }
    public decimal Score { get; init; }
    public decimal OpportunityScore { get; init; }
    public decimal Resistance { get; init; }
    public decimal VolumeSpike { get; init; }
    public decimal ResistanceDistance { get; init; }
    public decimal SupportDistance { get; init; }
    public decimal RiskReward { get; init; }
    public string TrendDirection { get; init; } = "";
    public bool IsBreakout { get; init; }
    public bool IsConsolidating { get; init; }
    public bool IsEliteSetup { get; init; }

    public string CloseFormatted => Close >= 1 ? Close.ToString("N2") : Close.ToString("N8");
    public string Signal => OpportunityScore >= 70 ? "STRONG BUY" :
                            OpportunityScore >= 55 ? "BUY" :
                            OpportunityScore >= 40 ? "WATCH" : "IGNORE";
    public string EliteText => IsEliteSetup ? "⭐" : "";
}
