using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class AtrLevelRule : IScoringRule
{
    public string Name => "AtrLevel";

    // Pontos de ancoragem: ATR extremamente baixo é pior que extremamente alto —
    // mercado parado gera mais sinal falso do que mercado agitado.
    private static readonly PercentileScoreCurve Curve = new(new List<(decimal, decimal)>
    {
        (0m, -20m),
        (10m, -20m),
        (25m, -10m),
        (75m, 0m),
        (90m, -5m),
        (100m, -10m)
    });

    public decimal Evaluate(ScoringContext context)
    {
        decimal? percentile = BollingerPercentileCalculator.CalculateCurrentPercentile(context.AtrPercentSeries);
        return percentile == null ? 0 : Curve.Evaluate(percentile.Value);
    }
}