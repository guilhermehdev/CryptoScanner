namespace CryptoScanner.Core.Models;

public sealed class FilterDiagnostics
{
    public int TotalAnalyzed { get; set; }
    public int PassedAll { get; set; }

    public int FailedScore { get; set; }
    public int FailedBreakout { get; set; }
    public int FailedConsolidation { get; set; }
    public int FailedVolumeSpike { get; set; }
    public int FailedResistanceDistance { get; set; }
    public int FailedDirection { get; set; }
    public int FailedRiskReward { get; set; }
    public int FailedStopDistance { get; set; }
    public int FailedStopDistanceTooHigh { get; set; }
    public int FailedRiskRewardTooHigh { get; set; }
    public int FailedBullTrap { get; set; }
    public int FailedTrendConfirmation { get; set; }

    // Filtro experimental (12/2026) — ver EligibilityThresholds.RequireBearishMomentumConfirmed.
    public int FailedMomentumFilter { get; set; }

    // Filtro experimental (22/08/2026) — ver EligibilityThresholds.BlockMeanReversionInBear.
    public int FailedMeanReversionRegimeFilter { get; set; }

    // Filtro experimental (28/08/2026) — ver EligibilityThresholds.LimitAtrForMeanReversion.
    public int FailedMeanReversionAtrFilter { get; set; }

    public int SkippedDuplicateToday { get; set; }

    public string Summary =>
        $"Score: {FailedScore} | Sem breakout: {FailedBreakout} | Sem consol.: {FailedConsolidation} | " +
        $"Vol. spike: {FailedVolumeSpike} | Dist. resist.: {FailedResistanceDistance} | " +
        $"Direção: {FailedDirection} | Risk/Reward: {FailedRiskReward} | Stop mín.: {FailedStopDistance} | " +
        $"Stop máx.: {FailedStopDistanceTooHigh} | RR teto: {FailedRiskRewardTooHigh} | Bull Trap: {FailedBullTrap} | " +
        $"Tendência (EMA): {FailedTrendConfirmation} | Momentum: {FailedMomentumFilter} | Regime MeanRev: {FailedMeanReversionRegimeFilter} | ATR MeanRev: {FailedMeanReversionAtrFilter} | " +
        $"Duplicado hoje: {SkippedDuplicateToday} | " +
        $"Passaram: {PassedAll}/{TotalAnalyzed}";
}