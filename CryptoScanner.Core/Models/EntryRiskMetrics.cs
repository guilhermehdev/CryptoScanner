using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

public sealed record EntryRiskMetrics(decimal TargetDistancePercent, decimal StopDistancePercent, decimal RiskReward)
{
    // O alvo do risco local é construído em 2R a partir do candle analisado. A entrada
    // sofre 0,05% de slippage adverso; rejeitar esse setup por uma diferença menor que
    // 0,05R faz o backtest eliminar quase todos os sinais antes de medir o resultado.
    // A tolerância não cobre gaps reais: o R/R planejado precisa cumprir o mínimo e a
    // perda máxima tolerada na execução é limitada a 0,05R.
    private const decimal IntradayLocalExecutionTolerance = 0.05m;

    public static EntryRiskMetrics Calculate(decimal entry, decimal stop, decimal target)
    {
        if (entry <= 0 || stop <= 0 || target <= 0 || stop >= entry || target <= entry)
            return new(0, 0, 0);
        return new((target-entry)/entry*100, (entry-stop)/entry*100, (target-entry)/(entry-stop));
    }

    public static EntryRiskMetrics CalculateDirectional(decimal entry, decimal stop, decimal target, TradeDirection direction)
    {
        if (entry <= 0 || stop <= 0 || target <= 0)
            return new(0, 0, 0);

        decimal targetDistance = direction == TradeDirection.Long ? target - entry : entry - target;
        decimal stopDistance = direction == TradeDirection.Long ? entry - stop : stop - entry;
        if (targetDistance <= 0 || stopDistance <= 0)
            return new(0, 0, 0);

        return new(targetDistance / entry * 100m, stopDistance / entry * 100m, targetDistance / stopDistance);
    }

    public static bool MeetsExecutionMinimum(
        decimal plannedRiskReward,
        decimal executionRiskReward,
        decimal minimumRiskReward,
        RiskCalculationMode riskMode)
    {
        if (executionRiskReward >= minimumRiskReward)
            return true;

        return riskMode == RiskCalculationMode.IntradayLocal &&
               plannedRiskReward >= minimumRiskReward &&
               executionRiskReward >= minimumRiskReward - IntradayLocalExecutionTolerance;
    }
}
