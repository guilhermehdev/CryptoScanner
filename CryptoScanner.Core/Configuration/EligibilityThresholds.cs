namespace CryptoScanner.Core.Configuration;

public sealed class EligibilityThresholds
{
    public required decimal BuyOpportunityScore { get; init; }
    public required decimal BearRegimePenalty { get; init; }
    public required decimal SidewaysRegimePenalty { get; init; }
    public required decimal MinVolumeSpike { get; init; }
    public required decimal DefensiveMinVolumeSpike { get; init; }
    public required decimal MinResistanceDistance { get; init; }
    public required decimal MinRiskReward { get; init; }
    public required decimal MinRelativeStrengthPercent { get; init; }
    public required decimal MinStopDistancePercent { get; init; }

    // Teto de RR — valores muito altos podem indicar resistência mal-calibrada,
    // não necessariamente uma oportunidade melhor. Default = sem teto (comportamento atual).
    public required decimal MaxRiskReward { get; init; }

    public static readonly EligibilityThresholds Default = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinRiskReward = ScannerSettings.MinRiskReward,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxRiskReward = decimal.MaxValue
    };
}