using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Configuration;

public static class ScannerSettings
{
    // Opportunity-score weights. They must total 1.00.
    public const decimal TrendWeight = 0.21m;
    public const decimal VolumeWeight = 0.17m;
    public const decimal StructureWeight = 0.21m;
    public const decimal CandleWeight = 0.13m;
    public const decimal SetupWeight = 0.13m;
    public const decimal MomentumWeight = 0.05m;
    public const decimal VolatilityWeight = 0.05m;
    public const decimal TrendStrengthWeight = 0.05m;

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

    // Modo defensivo (ativado quando marketRegime != "BULL")
    // Rompimento de curto prazo, em vez de resistência de 50 candles.
    public const int DefensiveBreakoutLookback = 8;

    // Volume spike mínimo relaxado, já que volume geral do mercado cai em bear/LATERAL.
    public const decimal DefensiveMinVolumeSpike = 1.10m;

    // Período (em candles de 1h) usado para comparar o retorno do ativo com o do BTC.
    public const int RelativeStrengthPeriodHours = 24;

    // Ativo precisa performar pelo menos igual ao BTC no período para contar como "força relativa".
    public const decimal MinRelativeStrengthPercent = 0m;

    // Penalidade aplicada ao Opportunity Score conforme o regime de mercado.
    public const decimal BearRegimePenalty = 10m;
    public const decimal SidewaysRegimePenalty = 8m;
}