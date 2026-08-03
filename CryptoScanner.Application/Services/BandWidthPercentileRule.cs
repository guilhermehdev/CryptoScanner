using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class BandWidthPercentileRule : IScoringRule
{
    public string Name => "BandWidthPercentile";

    public decimal Evaluate(ScoringContext context)
    {
        decimal? percentile = BollingerPercentileCalculator.CalculateCurrentPercentile(context.BollingerBandWidth);

        if (percentile == null)
            return 0; // sem histórico suficiente — não pontua nem penaliza

        decimal p = percentile.Value;

        if (p < 10)
        {
            // Squeeze extremo: ruim por padrão (mercado morto), a não ser que seja
            // rompimento de qualidade (mercado prestes a explodir).
            return IsSqueezeReversal(context) ? 30m : -35m;
        }

        if (p < 25)
        {
            // Compressão: critério de reversão mais rigoroso (4 condições), pra evitar
            // remover a penalidade por falso rompimento.
            return IsQualityCompressionBreakout(context) ? 0m : -15m;
        }

        if (p <= 75)
            return 0m; // zona normal — sem penalidade

        if (p <= 90)
            return -10m; // volatilidade alta

        // Explosão de volatilidade (>90): penalidade forte, com recuperação parcial se
        // houver força de tendência real por trás (não só ruído de notícia/liquidação).
        bool strongTrend = context.Trend.Adx >= 30m && context.Volume.Spike >= 1.5m;
        return strongTrend ? -5m : -20m;
    }

    private static bool IsSqueezeReversal(ScoringContext context)
    {
        if (context.Candles.Count == 0 || context.BollingerUpper.Count == 0 || context.BollingerLower.Count == 0)
            return false;

        var lastCandle = context.Candles[^1];
        decimal? upperBand = context.BollingerUpper[^1];
        decimal? lowerBand = context.BollingerLower[^1];

        if (upperBand == null || lowerBand == null)
            return false;

        bool closedOutsideBand = lastCandle.Close > upperBand.Value || lastCandle.Close < lowerBand.Value;
        bool volumeSurge = context.Volume.Spike >= 2.0m;

        return closedOutsideBand && volumeSurge;
    }

    private static bool IsQualityCompressionBreakout(ScoringContext context)
    {
        if (context.Candles.Count == 0 || context.BollingerUpper.Count == 0 || context.BollingerLower.Count == 0)
            return false;

        var lastCandle = context.Candles[^1];
        decimal? upperBand = context.BollingerUpper[^1];
        decimal? lowerBand = context.BollingerLower[^1];

        if (upperBand == null || lowerBand == null)
            return false;

        bool closedOutsideBand = lastCandle.Close > upperBand.Value || lastCandle.Close < lowerBand.Value;
        bool volumeSurge = context.Volume.Spike >= 1.8m;
        bool adxRising = context.Trend.Adx >= 25m; // aproximação — inclinação real fica pra Fase 3b
        bool bandExpanding = IsBandExpanding(context.BollingerBandWidth); // idem

        return closedOutsideBand && volumeSurge && adxRising && bandExpanding;
    }

    private static bool IsBandExpanding(List<decimal?> bandWidth)
    {
        var valid = bandWidth.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return valid.Count >= 2 && valid[^1] > valid[^2];
    }
}