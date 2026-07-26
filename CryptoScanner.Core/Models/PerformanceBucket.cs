namespace CryptoScanner.Core.Models;

public sealed class PerformanceBucket
{
    public required string Label { get; init; }
    public required int Count { get; init; }
    public required double WinRate { get; init; }
    public required double AvgReturn { get; init; }
}