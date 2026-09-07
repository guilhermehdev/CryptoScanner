namespace CryptoScanner.Core.Configuration;
public static class ScannerProfiles
{
    public static readonly EligibilityThresholds SwingValidatedThresholds = new()
    {
        EntryStrategy = EntryStrategy.Auto,
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance,
        MinResistanceDistancePartialExits = 4m,
        MinRiskReward = 2.0m,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxStopDistancePercent = 25m,
        MaxRiskReward = 999m,
        EnablePullbackBounce = true,
        EnableBollingerScoring = true,
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    public static readonly EligibilityThresholds IntradayValidatedThresholds = new()
    {
        EntryStrategy = EntryStrategy.Auto,
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance,
        MinResistanceDistancePartialExits = 15m,
        MinRiskReward = 2.5m,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxStopDistancePercent = 25m,
        MaxRiskReward = 999m,
        EnablePullbackBounce = false,
        EnableBollingerScoring = true,
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    public static readonly EligibilityThresholds ScalpValidatedThresholds = new()
    {
        EntryStrategy = EntryStrategy.Auto,
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance,
        MinResistanceDistancePartialExits = 20m,
        MinRiskReward = 3.0m,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxStopDistancePercent = 25m,
        MaxRiskReward = 999m,
        EnablePullbackBounce = false,
        EnableBollingerScoring = true,
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    public static EligibilityThresholds For(ScanProfile profile) =>
        profile.Name == ScanProfile.Intraday.Name ? IntradayValidatedThresholds :
        profile.Name == ScanProfile.Scalp.Name ? ScalpValidatedThresholds :
        SwingValidatedThresholds;


}
