using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Compara somente buckets com o mesmo nome entre exportações distintas.
/// Não escolhe parâmetros: torna explícito quando uma hipótese se repete em todas as janelas.
/// </summary>
public static class CandidateResearchWalkForwardComparer
{
    public static List<ResearchWalkForwardComparison> Compare(
        IEnumerable<(string Period, IEnumerable<ResearchCandidateSummary> Summaries)> periods)
    {
        var loaded = periods
            .Select(period => (period.Period, Summaries: period.Summaries.ToList()))
            .Where(period => period.Summaries.Count > 0)
            .ToList();

        if (loaded.Count == 0)
            return new List<ResearchWalkForwardComparison>();

        return loaded
            .SelectMany(period => period.Summaries.Select(summary => new { period.Period, Summary = summary }))
            .GroupBy(item => (item.Summary.Dimension, item.Summary.Bucket))
            .Select(group =>
            {
                var periodResults = group
                    .OrderBy(item => item.Period, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ResearchWalkForwardPeriodResult
                    {
                        Period = item.Period,
                        Count = item.Summary.Count,
                        AverageReturn24HoursPercent = item.Summary.AverageReturn24HoursPercent,
                        GrossProfitFactor24Hours = item.Summary.GrossProfitFactor24Hours
                    })
                    .ToList();

                int totalCandidates = periodResults.Sum(result => result.Count);
                decimal weightedReturn = totalCandidates == 0
                    ? 0
                    : periodResults.Sum(result => result.AverageReturn24HoursPercent * result.Count) / totalCandidates;
                int positivePeriods = periodResults.Count(result => result.AverageReturn24HoursPercent > 0 && result.GrossProfitFactor24Hours >= 1m);

                return new ResearchWalkForwardComparison
                {
                    Dimension = group.Key.Dimension,
                    Bucket = group.Key.Bucket,
                    LoadedPeriods = loaded.Count,
                    PositivePeriods = positivePeriods,
                    TotalCandidates = totalCandidates,
                    WeightedAverageReturn24HoursPercent = weightedReturn,
                    WorstReturn24HoursPercent = periodResults.Min(result => result.AverageReturn24HoursPercent),
                    LowestProfitFactor24Hours = periodResults.Min(result => result.GrossProfitFactor24Hours),
                    IsConsistentlyPositive = periodResults.Count == loaded.Count && positivePeriods == loaded.Count,
                    PeriodResults = periodResults
                };
            })
            .OrderByDescending(result => result.IsConsistentlyPositive)
            .ThenByDescending(result => result.PositivePeriods)
            .ThenByDescending(result => result.WeightedAverageReturn24HoursPercent)
            .ThenBy(result => result.Dimension, StringComparer.Ordinal)
            .ThenBy(result => result.Bucket, StringComparer.Ordinal)
            .ToList();
    }
}
