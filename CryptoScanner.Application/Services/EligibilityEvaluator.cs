using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public static class EligibilityEvaluator
{
    public sealed class EligibilityResult
    {
        public bool FailedScore { get; init; }
        public bool FailedBreakout { get; init; }
        public bool FailedConsolidation { get; init; }
        public bool FailedVolumeSpike { get; init; }
        public bool FailedResistanceDistance { get; init; }
        public bool FailedDirection { get; init; }
        public bool FailedRiskReward { get; init; }
        public bool FailedStopDistance { get; init; }
        public bool FailedStopDistanceTooHigh { get; init; }
        public bool FailedRiskRewardTooHigh { get; init; }
        public bool FailedBullTrap { get; init; }

        public bool IsEligible =>
            !FailedScore && !FailedBreakout && !FailedConsolidation &&
            !FailedVolumeSpike && !FailedResistanceDistance &&
            !FailedDirection && !FailedRiskReward && !FailedStopDistance &&
            !FailedStopDistanceTooHigh && !FailedRiskRewardTooHigh && !FailedBullTrap;
    }

    public static EligibilityResult Evaluate(AssetAnalysis asset, string marketRegime, EligibilityThresholds? thresholds = null)
    {
        thresholds ??= EligibilityThresholds.Default;

        bool defensiveMode = marketRegime != "BULL";

        decimal opportunity = marketRegime switch
        {
            "BEAR" => asset.OpportunityScore - thresholds.BearRegimePenalty,
            "LATERAL" => asset.OpportunityScore - thresholds.SidewaysRegimePenalty,
            _ => asset.OpportunityScore
        };

        bool failedScore = opportunity < thresholds.BuyOpportunityScore;

        bool passesClassicPaths = defensiveMode
            ? (asset.Setup.IsBreakout
                || asset.Setup.IsShortTermBreakout
                || asset.Setup.RelativeStrength >= thresholds.MinRelativeStrengthPercent)
            : asset.Setup.IsBreakout;

        bool passesPullbackBounce = thresholds.EnablePullbackBounce && asset.Setup.IsPullbackBounce;

        bool failedBreakout = !(passesClassicPaths || passesPullbackBounce);

        bool failedConsolidation = defensiveMode ? false : !asset.Setup.IsConsolidating;

        decimal volumeSpikeThreshold = defensiveMode
            ? thresholds.DefensiveMinVolumeSpike
            : thresholds.MinVolumeSpike;
        bool failedVolumeSpike = asset.Volume.Spike < volumeSpikeThreshold;

        decimal effectiveMinResistanceDistance = asset.Risk.Mode switch
        {
            RiskCalculationMode.AtrBased => thresholds.MinResistanceDistanceAtrMode,
            RiskCalculationMode.SwingWithPartialExits => thresholds.MinResistanceDistancePartialExits,
            _ => thresholds.MinResistanceDistance
        };
        bool failedResistanceDistance = asset.Risk.ResistanceDistancePercent < effectiveMinResistanceDistance;
        bool failedDirection = asset.Trend.Direction != "ALTA";
        bool failedRiskReward = asset.Risk.RiskReward < thresholds.MinRiskReward;
        bool failedStopDistance = asset.Risk.SupportDistancePercent < thresholds.MinStopDistancePercent;
        bool failedStopDistanceTooHigh = asset.Risk.SupportDistancePercent > thresholds.MaxStopDistancePercent;
        bool failedRiskRewardTooHigh = asset.Risk.RiskReward > thresholds.MaxRiskReward;

        bool failedBullTrap = asset.Structure.IsBullTrap;

        return new EligibilityResult
        {
            FailedScore = failedScore,
            FailedBreakout = failedBreakout,
            FailedConsolidation = failedConsolidation,
            FailedVolumeSpike = failedVolumeSpike,
            FailedResistanceDistance = failedResistanceDistance,
            FailedDirection = failedDirection,
            FailedRiskReward = failedRiskReward,
            FailedStopDistance = failedStopDistance,
            FailedStopDistanceTooHigh = failedStopDistanceTooHigh,
            FailedRiskRewardTooHigh = failedRiskRewardTooHigh,
            FailedBullTrap = failedBullTrap
        };
    }
}