using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Configuration;

public static class ScannerSettings
{
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
