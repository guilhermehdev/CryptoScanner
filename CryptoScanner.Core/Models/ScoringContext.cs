using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Models.Analysis;

public sealed class ScoringContext
{
    public required List<Candle> Candles { get; init; }
    public required TrendAnalysis Trend { get; init; }
    public required VolumeAnalysis Volume { get; init; }
    public required StructureAnalysis Structure { get; init; }
    public required RiskAnalysis Risk { get; init; }
    public required List<decimal?> BollingerMiddle { get; init; }
    public required List<decimal?> BollingerUpper { get; init; }
    public required List<decimal?> BollingerLower { get; init; }
    public required List<decimal?> BollingerBandWidth { get; init; }
    public required List<decimal?> AtrPercentSeries { get; init; }
    public required decimal? AdxSlope { get; init; }
    public required List<decimal?> CandleRangePercentSeries { get; init; }
}