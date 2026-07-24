using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public static class AssetScoreFactory
{
    public static AssetScore Create(AssetAnalysis analysis) => new()
    {
        Symbol = analysis.Symbol,
        Close = analysis.Trend.Close,
        Score = analysis.OpportunityScore,
        OpportunityScore = analysis.OpportunityScore,
        Resistance = analysis.Risk.Resistance,
        VolumeSpike = analysis.Volume.Spike,
        ResistanceDistance = analysis.Risk.ResistanceDistancePercent,
        SupportDistance = analysis.Risk.SupportDistancePercent,
        RiskReward = analysis.Risk.RiskReward,
        TrendDirection = analysis.Trend.Direction,
        IsBreakout = analysis.Setup.IsBreakout,
        IsConsolidating = analysis.Setup.IsConsolidating,
        IsEliteSetup = analysis.IsEliteSetup
    };
}
