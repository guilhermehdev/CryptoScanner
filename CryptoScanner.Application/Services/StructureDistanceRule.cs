using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;

namespace CryptoScanner.Application.Services;

public sealed class StructureDistanceRule : IScoringRule
{
    public string Name => "StructureDistance";

    private static readonly PercentileScoreCurve Curve = new(new List<(decimal, decimal)>
    {
        (0m, 20m),
        (0.3m, 20m),
        (0.7m, 10m),
        (1.2m, -15m),
        (3m, -15m)
    });

    public decimal Evaluate(ScoringContext context)
    {
        if (context.Candles.Count == 0 || context.AtrPercentSeries.Count == 0)
            return 0;

        decimal? currentAtrPercent = context.AtrPercentSeries[^1];
        if (currentAtrPercent == null || currentAtrPercent == 0)
            return 0;

        decimal close = context.Candles[^1].Close;
        decimal atrAbsolute = close * currentAtrPercent.Value / 100m;
        if (atrAbsolute == 0)
            return 0;

        decimal distanceToSupport = close - context.Risk.Support;
        decimal atrMultiples = distanceToSupport / atrAbsolute;

        return Curve.Evaluate(atrMultiples);
    }
}