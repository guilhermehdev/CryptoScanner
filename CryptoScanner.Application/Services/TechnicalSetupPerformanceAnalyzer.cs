using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public static class TechnicalSetupPerformanceAnalyzer
{
    private const int MinimumEvaluatedOccurrences = 30;

    public static IReadOnlyList<TechnicalSetupSummary> Build(IReadOnlyList<TechnicalSetupAlert> alerts) =>
        alerts
            .GroupBy(alert => new { alert.Setup, alert.Direction, alert.Profile, alert.MarketRegime })
            .Select(group =>
            {
                var evaluated = group.Where(alert => alert.IsEvaluated).ToList();
                decimal grossProfit = evaluated.Where(alert => alert.ReturnAfter24HoursPercent > 0).Sum(alert => alert.ReturnAfter24HoursPercent!.Value);
                decimal grossLoss = Math.Abs(evaluated.Where(alert => alert.ReturnAfter24HoursPercent < 0).Sum(alert => alert.ReturnAfter24HoursPercent!.Value));
                return new TechnicalSetupSummary
                {
                    Setup = group.Key.Setup,
                    Direction = group.Key.Direction,
                    Profile = group.Key.Profile,
                    MarketRegime = group.Key.MarketRegime,
                    Occurrences = group.Count(),
                    Evaluated = evaluated.Count,
                    EvidenceStatus = BuildEvidenceStatus(evaluated, grossProfit, grossLoss),
                    Positive24HoursPercent = evaluated.Count == 0 ? 0 : evaluated.Count(alert => alert.ReturnAfter24HoursPercent > 0) * 100m / evaluated.Count,
                    AverageReturn1HourPercent = evaluated.Count == 0 ? 0 : evaluated.Average(alert => alert.ReturnAfter1HourPercent ?? 0),
                    AverageReturn6HoursPercent = evaluated.Count == 0 ? 0 : evaluated.Average(alert => alert.ReturnAfter6HoursPercent ?? 0),
                    AverageReturn24HoursPercent = evaluated.Count == 0 ? 0 : evaluated.Average(alert => alert.ReturnAfter24HoursPercent ?? 0),
                    GrossProfitFactor24Hours = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999999m : 0,
                    AverageMaximumFavorable24HoursPercent = evaluated.Count == 0 ? 0 : evaluated.Average(alert => alert.MaximumFavorable24HoursPercent ?? 0),
                    AverageMaximumAdverse24HoursPercent = evaluated.Count == 0 ? 0 : evaluated.Average(alert => alert.MaximumAdverse24HoursPercent ?? 0)
                };
            })
            .OrderByDescending(summary => summary.Evaluated)
            .ThenByDescending(summary => summary.AverageReturn24HoursPercent)
            .ToList();

    private static string BuildEvidenceStatus(IReadOnlyCollection<TechnicalSetupAlert> evaluated, decimal grossProfit, decimal grossLoss)
    {
        if (evaluated.Count < MinimumEvaluatedOccurrences)
            return $"Coletando ({evaluated.Count}/{MinimumEvaluatedOccurrences})";

        decimal averageReturn = evaluated.Average(alert => alert.ReturnAfter24HoursPercent ?? 0);
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? decimal.MaxValue : 0;
        return averageReturn > 0 && profitFactor >= 1m
            ? "Observação favorável"
            : "Sem vantagem observada";
    }
}
