namespace CryptoScanner.Core.Models;

public sealed record EntryRiskMetrics(decimal TargetDistancePercent, decimal StopDistancePercent, decimal RiskReward)
{
    public static EntryRiskMetrics Calculate(decimal entry, decimal stop, decimal target)
    {
        if (entry <= 0 || stop <= 0 || target <= 0 || stop >= entry || target <= entry)
            return new(0, 0, 0);
        return new((target-entry)/entry*100, (entry-stop)/entry*100, (target-entry)/(entry-stop));
    }
}
