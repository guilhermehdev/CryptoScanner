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

    // Modo ATR de cálculo de risco (ainda não usado ao vivo — só testável no backtest).
    // Convenção comum de mercado: alvo = 2x o stop, garantindo RR=2 fixo por construção.
    public const decimal AtrStopMultiplier = 1.5m;
    public const decimal AtrTargetMultiplier = 5.0m;

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
    // Ajustado de 50 pra 170 — bate com o universo usado em toda a validação por Backtest
    // (Swing e Intraday, ambos calibrados em cima de 167-171 moedas). Com 50, o app ao vivo
    // rodava numa base ~3x menor do que a que validamos, tornando sinais elegíveis ainda
    // mais raros do que o esperado pelo Backtest.
    public const int MaxCoins = 170;

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

    // Buffer adicional (em múltiplos de ATR) afastando o stop do nível de suporte óbvio,
    // evitando ser stopado exatamente onde todo mundo já colocaria ordem de venda.
    public const decimal AtrBufferMultiplier = 0.5m;
}