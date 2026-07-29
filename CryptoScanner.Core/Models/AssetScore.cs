namespace CryptoScanner.Core.Models;

public sealed class AssetScore
{
    public string Symbol { get; init; } = "";
    public decimal Close { get; init; }
    public decimal Score { get; init; }
    public decimal OpportunityScore { get; init; }
    public decimal PreviousScore { get; init; }
    public decimal ScoreVariation { get; init; }
    public decimal Resistance { get; init; }
    public decimal VolumeSpike { get; init; }
    public decimal ResistanceDistance { get; init; }
    public decimal SupportDistance { get; init; }
    public decimal RiskReward { get; init; }
    public string TrendDirection { get; init; } = "";
    public bool IsBreakout { get; init; }
    public bool IsShortTermBreakout { get; init; }
    public decimal RelativeStrength { get; init; }
    public bool IsConsolidating { get; init; }
    public bool IsEliteSetup { get; init; }
    public bool HasExhaustion { get; init; }
    public string PatternName { get; init; } = "";
    public string BreakoutSource { get; init; } = "";
    public string MarketRegime { get; init; } = "";

    public int TrendScore { get; init; }
    public int StructureScore { get; init; }
    public int VolumeScore { get; init; }
    public int CandleScore { get; init; }
    public int SetupScore { get; init; }
    public int MomentumScore { get; init; }
    public int VolatilityScore { get; init; }
    public int TrendStrengthScore { get; init; }
    public string SmartMoneyLabel { get; init; } = "";
    public bool IsBullTrap { get; init; }
    public bool IsBearTrap { get; init; }
    public bool IsEligible { get; init; }
    public bool IsFavorite { get; set; }
    public string CloseFormatted => Close >= 1 ? Close.ToString("N2") : Close.ToString("N8");
    public string Signal => OpportunityScore >= 70 ? "COMPRA+" :
                        OpportunityScore >= 55 ? "COMPRA" :
                        OpportunityScore >= 40 ? "MONITORAR" : "IGNORAR";
    public string EliteText => IsEliteSetup ? "⭐" : "";

    public string VariationText =>
        ScoreVariation > 0 ? $"▲ {ScoreVariation:F2}" :
        ScoreVariation < 0 ? $"▼ {Math.Abs(ScoreVariation):F2}" :
        "— 0.00";

    public string RelativeStrengthText =>
        RelativeStrength >= 0 ? $"+{RelativeStrength:F2}% vs BTC" : $"{RelativeStrength:F2}% vs BTC";

    // Consolidação só é exigida como critério de elegibilidade quando o regime é BULL.
    public bool IsConsolidationRelevant => MarketRegime == "BULL";
}