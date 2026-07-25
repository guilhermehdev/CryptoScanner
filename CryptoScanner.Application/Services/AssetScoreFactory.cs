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
        PreviousScore = analysis.PreviousScore,
        ScoreVariation = analysis.ScoreVariation,
        Resistance = analysis.Risk.Resistance,
        VolumeSpike = analysis.Volume.Spike,
        ResistanceDistance = analysis.Risk.ResistanceDistancePercent,
        SupportDistance = analysis.Risk.SupportDistancePercent,
        RiskReward = analysis.Risk.RiskReward,
        TrendDirection = analysis.Trend.Direction,
        IsBreakout = analysis.Setup.IsBreakout,
        IsShortTermBreakout = analysis.Setup.IsShortTermBreakout,
        RelativeStrength = analysis.Setup.RelativeStrength,
        IsConsolidating = analysis.Setup.IsConsolidating,
        IsEliteSetup = analysis.IsEliteSetup,
        TrendScore = analysis.Trend.Score,
        StructureScore = analysis.Structure.Score,
        VolumeScore = analysis.Volume.Score,
        CandleScore = analysis.Candle.Score,
        SetupScore = analysis.Setup.Score,
        MomentumScore = analysis.Trend.MomentumScore,
        VolatilityScore = analysis.Trend.VolatilityScore,
        TrendStrengthScore = analysis.Trend.TrendStrengthScore
    };
}