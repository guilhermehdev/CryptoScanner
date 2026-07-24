using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Models;

public sealed class ScannerRunResult
{
    public required string MarketRegime { get; init; }
    public required IReadOnlyList<AssetScore> Ranking { get; init; }
    public required IReadOnlyList<SignalHistory> History { get; init; }
    public required double WinRate { get; init; }
    public required double AverageReturn { get; init; }
}
