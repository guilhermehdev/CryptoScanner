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

        public bool IsEligible =>
            !FailedScore && !FailedBreakout && !FailedConsolidation &&
            !FailedVolumeSpike && !FailedResistanceDistance &&
            !FailedDirection && !FailedRiskReward;
    }

    public static EligibilityResult Evaluate(AssetAnalysis asset, string marketRegime)
    {
        bool defensiveMode = marketRegime != "BULL";

        decimal opportunity = marketRegime switch
        {
            "BEAR" => asset.OpportunityScore - ScannerSettings.BearRegimePenalty,
            "LATERAL" => asset.OpportunityScore - ScannerSettings.SidewaysRegimePenalty,
            _ => asset.OpportunityScore
        };

        bool failedScore = opportunity < ScannerSettings.BuyOpportunityScore;

        bool passesBreakout = defensiveMode
            ? (asset.Setup.IsBreakout
                || asset.Setup.IsShortTermBreakout
                || asset.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent)
            : asset.Setup.IsBreakout;
        bool failedBreakout = !passesBreakout;

        bool failedConsolidation = defensiveMode ? false : !asset.Setup.IsConsolidating;

        decimal volumeSpikeThreshold = defensiveMode
            ? ScannerSettings.DefensiveMinVolumeSpike
            : ScannerSettings.MinVolumeSpike;
        bool failedVolumeSpike = asset.Volume.Spike < volumeSpikeThreshold;

        bool failedResistanceDistance = asset.Risk.ResistanceDistancePercent < ScannerSettings.MinResistanceDistance;
        bool failedDirection = asset.Trend.Direction != "ALTA";
        bool failedRiskReward = asset.Risk.RiskReward < ScannerSettings.MinRiskReward;

        return new EligibilityResult
        {
            FailedScore = failedScore,
            FailedBreakout = failedBreakout,
            FailedConsolidation = failedConsolidation,
            FailedVolumeSpike = failedVolumeSpike,
            FailedResistanceDistance = failedResistanceDistance,
            FailedDirection = failedDirection,
            FailedRiskReward = failedRiskReward
        };
    }
}