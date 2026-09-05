namespace CryptoScanner.Core.Models;

public sealed record PressureAnalysisFilter(long FromMs, long ToMs, string Symbol, int HorizonMinutes, string FormulaVersion);

public sealed record PressureBand(int Band, long Readings, long Evaluated, long Pending, long Overdue,
    long Positive, long Reconstructed, decimal? AverageReturn, decimal? MinReturn, decimal? MaxReturn)
{
    public string Range => Band == 9 ? "90–100" : $"{Band * 10}–<{(Band + 1) * 10}";
    public decimal? PositivePercent => Evaluated == 0 ? null : Positive * 100m / Evaluated;
}

public sealed record PressureHistoryRow(long Id, string Symbol, long WindowEndMs, long CollectedAtMs,
    decimal? Score, decimal? ReferencePrice, decimal? ReturnPercent, string Status, string Source, string Details)
{
    public DateTime WindowLocal => DateTimeOffset.FromUnixTimeMilliseconds(WindowEndMs).LocalDateTime;
    public DateTime CollectedLocal => DateTimeOffset.FromUnixTimeMilliseconds(CollectedAtMs).LocalDateTime;
}

public sealed record PressureAnalysisReport(long TotalReadings, long Unavailable,
    IReadOnlyList<PressureBand> Bands, IReadOnlyList<PressureHistoryRow> History);
