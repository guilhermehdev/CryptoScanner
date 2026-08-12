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
    public int SkippedDuplicateToday { get; set; }

    public string Summary =>
        $"Score: {FailedScore} | Sem breakout: {FailedBreakout} | Sem consol.: {FailedConsolidation} | " +
        $"Vol. spike: {FailedVolumeSpike} | Dist. resist.: {FailedResistanceDistance} | " +
        $"Direção: {FailedDirection} | Risk/Reward: {FailedRiskReward} | Stop mín.: {FailedStopDistance} | " +
        $"Stop máx.: {FailedStopDistanceTooHigh} | RR teto: {FailedRiskRewardTooHigh} | Bull Trap: {FailedBullTrap} | " +
        $"Tendência (EMA): {FailedTrendConfirmation} | " +
        $"Duplicado hoje: {SkippedDuplicateToday} | " +
        $"Passaram: {PassedAll}/{TotalAnalyzed}";
}