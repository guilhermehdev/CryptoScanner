using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class CandleRangeRule : IScoringRule
{
    public string Name => "CandleRange";

    public decimal Evaluate(ScoringContext context)
    {
        decimal? percentile = BollingerPercentileCalculator.CalculateCurrentPercentile(context.CandleRangePercentSeries);
        if (percentile == null)
            return 0;

        decimal p = percentile.Value;

        if (p < 10)
            return -5m; // candle muito apertado — indecisão

        if (p <= 90)
            return 0m; // normal

        // Candle muito largo: rompimento saudável (com volume) ou pavio sem convicção.
        return context.Volume.Spike >= 1.8m ? 5m : -10m;
    }
}