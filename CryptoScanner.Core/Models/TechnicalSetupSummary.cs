using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

public sealed class TechnicalSetupSummary
{
    public required string Setup { get; init; }
    public required TradeDirection Direction { get; init; }
    public required string Profile { get; init; }
    public required string MarketRegime { get; init; }
    public required int Occurrences { get; init; }
    public required int Evaluated { get; init; }
    public required string EvidenceStatus { get; init; }
    public required decimal Positive24HoursPercent { get; init; }
    public required decimal AverageReturn1HourPercent { get; init; }
    public required decimal AverageReturn6HoursPercent { get; init; }
    public required decimal AverageReturn24HoursPercent { get; init; }
    public required decimal GrossProfitFactor24Hours { get; init; }
    public required decimal AverageMaximumFavorable24HoursPercent { get; init; }
    public required decimal AverageMaximumAdverse24HoursPercent { get; init; }
}
