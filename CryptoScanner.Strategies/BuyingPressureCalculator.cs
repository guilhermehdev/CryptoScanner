using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

/// <summary>Experimental confirmation over 30 minutes, not a calibrated buy probability.</summary>
public static class BuyingPressureCalculator
{
    private const long Interval = 300_000;

    public static BuyingPressureResult Calculate(MarketFlowData flow, DateTimeOffset now)
    {
        var candles = flow.PressureCandles.OrderBy(c => c.OpenTime).ToArray();
        var history = flow.OpenInterestHistory.OrderBy(p => p.Timestamp).ToArray();
        long nowMs = now.ToUnixTimeMilliseconds();
        var closed = candles.Where(c => c.OpenTime + Interval <= nowMs).ToArray();
        if (closed.Length < 26 || history.Length < 2)
            return BuyingPressureResult.Unavailable("histórico insuficiente de preço, compras ou OI.");

        // Align both sources to a completed 5-minute boundary. Allow one delayed OI update.
        long end = Math.Min(closed[^1].OpenTime + Interval, history[^1].Timestamp / Interval * Interval);
        if (end > nowMs || nowMs - end > 2 * Interval)
            return BuyingPressureResult.Unavailable("cotação ou OI desatualizado.");
        var sample = closed.Where(c => c.OpenTime < end).TakeLast(26).ToArray();
        if (sample.Length != 26 || sample[^1].OpenTime + Interval != end ||
            sample.Where((c, i) => c.OpenTime != end - (26 - i) * Interval).Any())
            return BuyingPressureResult.Unavailable("lacunas nos períodos de cinco minutos.");
        if (sample.Any(c => c.Open <= 0 || c.Close <= 0 || c.Low <= 0 ||
            c.High < Math.Max(c.Open, c.Close) || c.Low > Math.Min(c.Open, c.Close) ||
            c.Volume <= 0 || c.BuyVolume < 0 || c.BuyVolume > c.Volume))
            return BuyingPressureResult.Unavailable("preço ou volume inválido.");

        // OI timestamps can carry sub-minute offsets; pair samples within the same bucket.
        var oiStart = history.LastOrDefault(p => p.Timestamp / Interval * Interval == end - 6 * Interval);
        var oiEnd = history.LastOrDefault(p => p.Timestamp / Interval * Interval == end);
        if (oiStart is null || oiEnd is null || oiStart.Value <= 0 || oiEnd.Value <= 0)
            return BuyingPressureResult.Unavailable("OI sem amostras alinhadas aos últimos 30 minutos.");

        var baseline = sample[..20];
        var recent = sample[20..];
        decimal buyRatio = recent.Sum(c => c.BuyVolume) / recent.Sum(c => c.Volume);
        decimal persistence = recent.Average(c => c.BuyVolume / c.Volume > .5m ? 1m :
            c.BuyVolume / c.Volume == .5m ? .5m : 0m);
        decimal atr = baseline.Select((c, i) => Math.Max(c.High - c.Low,
            Math.Max(Math.Abs(c.High - (i == 0 ? c.Open : baseline[i - 1].Close)),
                     Math.Abs(c.Low - (i == 0 ? c.Open : baseline[i - 1].Close))))).Average();
        if (atr <= 0)
            return BuyingPressureResult.Unavailable("variação de preço insuficiente.");

        decimal move = recent[^1].Close - recent[0].Open;
        decimal priceChange = move / recent[0].Open * 100m;
        decimal volumeRatio = recent.Average(c => c.Volume) / baseline.Average(c => c.Volume);
        decimal oiChange = (oiEnd.Value - oiStart.Value) / oiStart.Value * 100m;
        decimal baselinePrice = baseline.Sum(c => c.Close * c.Volume) / baseline.Sum(c => c.Volume);
        decimal extension = Math.Max(0m, (recent[^1].Close - baselinePrice) / atr);
        decimal penalty = Math.Clamp((extension - 3m) * 5m, 0m, 25m);

        decimal aggression = Math.Clamp(50m + (buyRatio - .5m) * 250m, 0m, 100m);
        decimal response = Math.Clamp(50m + move / atr * 25m, 0m, 100m);
        // Volume and OI confirm the direction of price; neither rewards a falling market.
        decimal direction = Math.Sign(move);
        decimal volumeScore = 50m + direction * Math.Clamp((volumeRatio - 1m) * 50m, 0m, 50m);
        decimal oiScore = 50m + direction * Math.Clamp(oiChange * 25m, 0m, 50m);
        decimal score = aggression * .45m + persistence * 100m * .15m + response * .20m +
            volumeScore * .10m + oiScore * .10m - penalty;
        if (buyRatio <= .5m || move <= 0m) score = Math.Min(score, 50m);
        if (oiChange <= 0m) score = Math.Min(score, 75m);

        return new(Math.Round(Math.Clamp(score, 0m, 100m), 2),
            $"Pressão compradora nos futuros — 30 min até {DateTimeOffset.FromUnixTimeMilliseconds(end).ToLocalTime():HH:mm}.\n" +
            $"Compras agressivas: {buyRatio:P1} | Predomínio comprador: {recent.Count(c => c.BuyVolume > c.Volume / 2m)}/6 períodos\n" +
            $"Preço: {priceChange:+0.00;-0.00;0.00}% | Volume: {volumeRatio:F2}× | OI: {oiChange:+0.00;-0.00;0.00}%\n" +
            $"Penalização por esticamento: {penalty:F1} pontos.\n" +
            "Experimental: não é probabilidade de acerto nem identifica varejo. Sem corte automático de compra.");
    }
}
