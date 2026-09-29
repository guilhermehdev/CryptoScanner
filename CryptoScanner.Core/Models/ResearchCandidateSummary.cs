namespace CryptoScanner.Core.Models;

/// <summary>Resumo agregado da pesquisa de candidatos para comparação entre janelas históricas.</summary>
public sealed class ResearchCandidateSummary
{
    public required string Dimension { get; init; }
    public required string Bucket { get; init; }
    public required int Count { get; init; }
    public required decimal Positive24HoursPercent { get; init; }
    public required decimal AverageReturn6HoursPercent { get; init; }
    public required decimal AverageReturn24HoursPercent { get; init; }
    public required decimal GrossProfitFactor24Hours { get; init; }
    public required decimal AverageMaximumFavorable24HoursPercent { get; init; }
    public required decimal AverageMaximumAdverse24HoursPercent { get; init; }
}
