using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

public sealed record EntryRiskMetrics(decimal TargetDistancePercent, decimal StopDistancePercent, decimal RiskReward)
{
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
}
