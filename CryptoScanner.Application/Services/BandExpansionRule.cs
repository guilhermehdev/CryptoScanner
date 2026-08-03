using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;

namespace CryptoScanner.Application.Services;

public sealed class BandExpansionRule : IScoringRule
{
    private const int SlopeLookback = 5;

    public string Name => "BandExpansion";

    private static readonly PercentileScoreCurve Curve = new(new List<(decimal, decimal)>
    {
        (-100m, -5m),
        (0m, 0m),
        (50m, 10m),
        (300m, 10m)
    });

    public decimal Evaluate(ScoringContext context)
    {
        var valid = context.BollingerBandWidth.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (valid.Count <= SlopeLookback)
            return 0;

        decimal previous = valid[^(SlopeLookback + 1)];
        decimal current = valid[^1];

        if (previous == 0)
            return 0;

        // Variação % da largura de banda — distingue expansão suave (1.5→1.9)
        // de explosão (1.5→15).
        decimal percentChange = (current - previous) / previous * 100m;
        return Curve.Evaluate(percentChange);
    }
}