using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public sealed class AtrSlopeRule : IScoringRule
{
    private const int SlopeLookback = 5;

    public string Name => "AtrSlope";

    public decimal Evaluate(ScoringContext context)
    {
        decimal? atrSlope = CalculateSlope(context.AtrPercentSeries, SlopeLookback);

        if (atrSlope == null || context.AdxSlope == null)
            return 0;

        if (atrSlope <= 0)
            return 0; // ATR caindo — mercado esfriando, neutro

        decimal adxLevel = context.Trend.Adx;
        decimal adxSlope = context.AdxSlope.Value;

        if (adxLevel >= 25m && adxSlope > 0)
            return 10m;

        if (adxLevel < 25m && adxSlope > 0)
            return 3m;

        return -10m;
    }

    private static decimal? CalculateSlope(List<decimal?> series, int lookback)
    {
        var valid = series.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (valid.Count <= lookback)
            return null;

        return valid[^1] - valid[^(lookback + 1)];
    }
}