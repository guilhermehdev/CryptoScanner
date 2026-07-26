using System.Collections.Generic;

namespace CryptoScanner.Core.Models;

public sealed class PerformanceReport
{
    public required int TotalEvaluated { get; init; }
    public required List<PerformanceBucket> ByScore { get; init; }
    public required List<PerformanceBucket> ByRsi { get; init; }
    public required List<PerformanceBucket> ByAdx { get; init; }
    public required List<PerformanceBucket> ByAtrPercent { get; init; }
    public required List<PerformanceBucket> ByPattern { get; init; }
    public required List<PerformanceBucket> BySmartMoney { get; init; }
    public required List<PerformanceBucket> ByBreakoutSource { get; init; }
    public required List<PerformanceBucket> ByMarketRegime { get; init; }
    public required List<PerformanceBucket> ByProfile { get; init; }
    public required List<PerformanceBucket> ByExitReason { get; init; }
}