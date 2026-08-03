using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;

namespace CryptoScanner.Application.Services;

public sealed class LiquidityRule : IScoringRule
{
    public string Name => "Liquidity";

    private static readonly PercentileScoreCurve Curve = new(new List<(decimal, decimal)>
    {
        (0m, -30m),
        (500_000m, -30m),
        (10_000_000m, 0m),
        (100_000_000m, 15m),
        (1_000_000_000m, 15m)
    });

    public decimal Evaluate(ScoringContext context)
    {
        if (context.Candles.Count == 0)
            return 0;

        // O Candle guarda volume na moeda-base, não em USD direto — aproxima
        // multiplicando pelo preço de fechamento de cada candle (volume × close ≈
        // volume em quote/USD pra pares XXXUSDT).
        int candlesIn24h = Estimate24hCandleCount(context.Candles);
        var recent = context.Candles.TakeLast(Math.Min(candlesIn24h, context.Candles.Count)).ToList();

        decimal quoteVolumeUsd = recent.Sum(c => c.Volume * c.Close);
        return Curve.Evaluate(quoteVolumeUsd);
    }

    private static int Estimate24hCandleCount(List<Candle> candles)
    {
        if (candles.Count < 2)
            return 1;

        var span = candles[^1].OpenTime - candles[^2].OpenTime;
        if (span.TotalHours <= 0)
            return 1;

        return Math.Max(1, (int)Math.Round(24 / span.TotalHours));
    }
}