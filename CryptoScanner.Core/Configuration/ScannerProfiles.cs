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

    /// <summary>
    /// Returns the thresholds for the requested scanner side.  The experimental
    /// Short profile is opt-in so the existing Long and Short behaviour remain
    /// unchanged until the operator explicitly enables it in the UI.
    /// </summary>
    public static EligibilityThresholds For(ScanProfile profile, TradeDirection direction, bool shortExperimental)
    {
        var baseThresholds = For(profile);
        if (direction != TradeDirection.Short || !shortExperimental)
            return baseThresholds;

        return new EligibilityThresholds
        {
            IsolatedEntryExperiment = baseThresholds.IsolatedEntryExperiment,
            TargetZoneExperiment = baseThresholds.TargetZoneExperiment,
            StructuralEntryExperiment = baseThresholds.StructuralEntryExperiment,
            MinimumTargetAtr = baseThresholds.MinimumTargetAtr,
            EntryStrategy = baseThresholds.EntryStrategy,
            BuyOpportunityScore = baseThresholds.BuyOpportunityScore,
            BearRegimePenalty = baseThresholds.BearRegimePenalty,
            SidewaysRegimePenalty = baseThresholds.SidewaysRegimePenalty,
            MinVolumeSpike = baseThresholds.MinVolumeSpike,
            DefensiveMinVolumeSpike = baseThresholds.DefensiveMinVolumeSpike,
            MinResistanceDistance = baseThresholds.MinResistanceDistance,
            EnableMultiTimeframe = baseThresholds.EnableMultiTimeframe,
            MinResistanceDistanceAtrMode = baseThresholds.MinResistanceDistanceAtrMode,
            MinRiskReward = baseThresholds.MinRiskReward,
            MinRelativeStrengthPercent = baseThresholds.MinRelativeStrengthPercent,
            MinStopDistancePercent = baseThresholds.MinStopDistancePercent,
            MaxStopDistancePercent = baseThresholds.MaxStopDistancePercent,
            MaxShortOpportunityScore = 80m,
            MaxShortAdxInBear = 30m,
            MaxRiskReward = baseThresholds.MaxRiskReward,
            EnablePullbackBounce = baseThresholds.EnablePullbackBounce,
            EnableBollingerScoring = baseThresholds.EnableBollingerScoring,
            EnableVolatilityScoringPhaseB = baseThresholds.EnableVolatilityScoringPhaseB,
            MinResistanceDistancePartialExits = baseThresholds.MinResistanceDistancePartialExits,
            EnableMeanReversionScalp = baseThresholds.EnableMeanReversionScalp,
            EnableBollingerReversal = baseThresholds.EnableBollingerReversal,
            RequireBearishMomentumConfirmed = true,
            BlockShortInSideways = true,
            EnableLowRsiPath = baseThresholds.EnableLowRsiPath,
            BlockMeanReversionInBear = baseThresholds.BlockMeanReversionInBear,
            LimitAtrForMeanReversion = baseThresholds.LimitAtrForMeanReversion,
        };
    }


}
