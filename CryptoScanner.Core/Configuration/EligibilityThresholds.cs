namespace CryptoScanner.Core.Configuration;

public sealed class EligibilityThresholds
{
    public required decimal BuyOpportunityScore { get; init; }
    public required decimal BearRegimePenalty { get; init; }
    public required decimal SidewaysRegimePenalty { get; init; }
    public required decimal MinVolumeSpike { get; init; }
    public required decimal DefensiveMinVolumeSpike { get; init; }
    public required decimal MinResistanceDistance { get; init; }

    // Limiar separado pro modo ATR — Res% aqui mede volatilidade (ATR% × multiplicador),
    // não distância estrutural até um topo real. Precisa de escala diferente.
    public required decimal MinResistanceDistanceAtrMode { get; init; }

    public required decimal MinRiskReward { get; init; }
    public required decimal MinRelativeStrengthPercent { get; init; }
    public required decimal MinStopDistancePercent { get; init; }
    public required decimal MaxRiskReward { get; init; }
    public required bool EnablePullbackBounce { get; init; }
    public required bool EnableBollingerScoring { get; init; }
    public required bool EnableVolatilityScoringPhaseB { get; init; }

    public static readonly EligibilityThresholds Default = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // provisório — modo ATR ainda não é usado ao vivo
        MinRiskReward = ScannerSettings.MinRiskReward,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxRiskReward = decimal.MaxValue,
        EnablePullbackBounce = false,
        EnableBollingerScoring = false,
        EnableVolatilityScoringPhaseB = false,
    };
}