namespace CryptoScanner.Core.Models;

/// <summary>
/// Comparação do mesmo fator entre janelas históricas independentes.
/// Um resultado consistente é informativo, mas nunca altera a estratégia por si só.
/// </summary>
public sealed class ResearchWalkForwardComparison
{
    public required string Dimension { get; init; }
    public required string Bucket { get; init; }
    public required int LoadedPeriods { get; init; }
    public required int PositivePeriods { get; init; }
    public required int TotalCandidates { get; init; }
    public required decimal WeightedAverageReturn24HoursPercent { get; init; }
    public required decimal WorstReturn24HoursPercent { get; init; }
    public required decimal LowestProfitFactor24Hours { get; init; }
    public required bool IsConsistentlyPositive { get; init; }
    public required IReadOnlyList<ResearchWalkForwardPeriodResult> PeriodResults { get; init; }
}

public sealed class ResearchWalkForwardPeriodResult
{
    public required string Period { get; init; }
    public required int Count { get; init; }
    public required decimal AverageReturn24HoursPercent { get; init; }
    public required decimal GrossProfitFactor24Hours { get; init; }
}
