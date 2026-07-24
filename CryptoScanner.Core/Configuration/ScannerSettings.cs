using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Configuration;

public static class ScannerSettings
{
    // Opportunity-score weights. They must total 1.00.
    public const decimal TrendWeight = 0.25m;
    public const decimal VolumeWeight = 0.20m;
    public const decimal StructureWeight = 0.25m;
    public const decimal CandleWeight = 0.15m;
    public const decimal SetupWeight = 0.15m;

    // Scores
    public const decimal EliteOpportunityScore = 85m;
    public const decimal BuyOpportunityScore = 60m;

    // Tendência
    public const decimal MaxRejectionScore = 0.40m;

    // Volume
    public const decimal MinRelativeVolume = 1.5m;
    public const decimal MinVolumeSpike = 1.30m;

    // Risco
    public const decimal MinRiskReward = 3m;

    // Espaço até resistência
    public const decimal MinResistanceDistance = 8m;

    // Histórico
    public const int EvaluationHours = 24;

    // Scanner
    public const int MaxCoins = 50;
}
