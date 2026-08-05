using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;

namespace CryptoScanner.Application.Services;

public sealed class StructureDistanceRule : IScoringRule
{
    public string Name => "StructureDistance";

    // Curva invertida em relação à primeira versão: agora recompensa FOLGA em relação
    // ao suporte, não proximidade — a versão original (premiando entrada colada no
    // suporte) se mostrou ligada a RR inflado e Win Rate ruim em teste real, e conflitava
    // filosoficamente com o modo Swing+Buffer ATR, que já afasta o stop de propósito.
    private static readonly PercentileScoreCurve Curve = new(new List<(decimal, decimal)>
    {
        (0m, -15m),
        (0.3m, -15m),
        (0.7m, 0m),
        (1.2m, 10m),
        (2.5m, 15m)
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