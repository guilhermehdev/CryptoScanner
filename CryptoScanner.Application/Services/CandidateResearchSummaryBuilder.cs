using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Constrói um resumo compacto e repetível para comparar a mesma pesquisa entre períodos.
/// Não escolhe filtros nem altera a estratégia; só agrega as observações já exportadas.
/// </summary>
public static class CandidateResearchSummaryBuilder
{
    public static List<ResearchCandidateSummary> Build(IEnumerable<BacktestResearchCandidate> candidates)
    {
        var list = candidates.ToList();
        return BuildDimension("Todos", list.Select(candidate => ("Todos", candidate)))
            .Concat(BuildDimension("Filtros", list.Select(candidate => (candidate.PassedAllFilters ? "Passou filtros" : "Rejeitado", candidate))))
            .Concat(BuildDimension("Regime", list.Select(candidate => (string.IsNullOrWhiteSpace(candidate.MarketRegime) ? "(sem regime)" : candidate.MarketRegime, candidate))))
            .Concat(BuildDimension("ATR%", list.Select(candidate => (AtrBucket(candidate.AtrPercent), candidate))))
            .Concat(BuildDimension("RSI", list.Select(candidate => (RsiBucket(candidate.Rsi), candidate))))
            .Concat(BuildDimension("ADX", list.Select(candidate => (AdxBucket(candidate.Adx), candidate))))
            .Concat(BuildDimension("Volume", list.Select(candidate => (VolumeBucket(candidate.VolumeSpike), candidate))))
            .OrderBy(summary => summary.Dimension).ThenBy(summary => summary.Bucket, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<ResearchCandidateSummary> BuildDimension(string dimension, IEnumerable<(string Bucket, BacktestResearchCandidate Candidate)> source) =>
        source.GroupBy(item => item.Bucket).Select(group =>
        {
            var items = group.Select(item => item.Candidate).ToList();
            decimal grossProfit = items.Where(item => item.ReturnAfter24HoursPercent > 0).Sum(item => item.ReturnAfter24HoursPercent);
            decimal grossLoss = -items.Where(item => item.ReturnAfter24HoursPercent < 0).Sum(item => item.ReturnAfter24HoursPercent);
            return new ResearchCandidateSummary
            {
                Dimension = dimension,
                Bucket = group.Key,
                Count = items.Count,
                Positive24HoursPercent = items.Count == 0 ? 0 : items.Count(item => item.ReturnAfter24HoursPercent > 0) * 100m / items.Count,
                AverageReturn6HoursPercent = items.Count == 0 ? 0 : items.Average(item => item.ReturnAfter6HoursPercent),
                AverageReturn24HoursPercent = items.Count == 0 ? 0 : items.Average(item => item.ReturnAfter24HoursPercent),
                GrossProfitFactor24Hours = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999999m : 0m,
                AverageMaximumFavorable24HoursPercent = items.Count == 0 ? 0 : items.Average(item => item.MaximumFavorable24HoursPercent),
                AverageMaximumAdverse24HoursPercent = items.Count == 0 ? 0 : items.Average(item => item.MaximumAdverse24HoursPercent)
            };
        });

    private static string AtrBucket(decimal value) => value < 1 ? "<1%" : value < 2 ? "1-2%" : value < 3 ? "2-3%" : "≥3%";
    private static string RsiBucket(decimal value) => value < 30 ? "<30" : value < 40 ? "30-40" : value < 50 ? "40-50" : value < 60 ? "50-60" : value < 70 ? "60-70" : "≥70";
    private static string AdxBucket(decimal value) => value < 20 ? "<20" : value < 30 ? "20-30" : value < 40 ? "30-40" : "≥40";
    private static string VolumeBucket(decimal value) => value < 1.2m ? "<1,2x" : value < 1.5m ? "1,2-1,5x" : value < 2m ? "1,5-2x" : "≥2x";
}
