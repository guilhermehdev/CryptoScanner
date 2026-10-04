using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

/// <summary>
/// Identifica situações técnicas observacionais. Elas descrevem o contexto do candle
/// fechado e não são regras de entrada, score ou execução.
/// </summary>
public static class TechnicalSetupAlertDetector
{
    public static IReadOnlyList<string> Detect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<decimal?> upper,
        IReadOnlyList<decimal?> lower,
        IReadOnlyList<decimal?> widthPercent,
        decimal atr,
        decimal volumeSpike,
        TradeDirection direction)
    {
        var alerts = new List<string>();
        if (candles.Count < 2 || upper.Count < candles.Count || lower.Count < candles.Count)
            return alerts;

        int current = candles.Count - 1;
        int previous = current - 1;
        var last = candles[current];
        var prior = candles[previous];
        decimal? currentUpper = upper[current];
        decimal? currentLower = lower[current];
        decimal? previousUpper = upper[previous];
        decimal? previousLower = lower[previous];

        if (currentUpper.HasValue && currentLower.HasValue && previousUpper.HasValue && previousLower.HasValue)
        {
            if (direction == TradeDirection.Long)
            {
                if (prior.Close < previousLower.Value && last.Close >= currentLower.Value && last.Close > last.Open)
                    alerts.Add("FFFD compra");

                if (last.Open <= currentLower.Value && last.Low <= currentLower.Value && last.Close > last.Open)
                    alerts.Add("RFB compra");

                if (IsNarrow(widthPercent, previous) && last.Low < currentLower.Value &&
                    last.Close > currentLower.Value && last.Close > last.Open)
                    alerts.Add("Estilingue compra");
            }
            else
            {
                if (prior.Close > previousUpper.Value && last.Close <= currentUpper.Value && last.Close < last.Open)
                    alerts.Add("FFFD venda");

                if (last.Open >= currentUpper.Value && last.High >= currentUpper.Value && last.Close < last.Open)
                    alerts.Add("RFB venda");

                if (IsNarrow(widthPercent, previous) && last.High > currentUpper.Value &&
                    last.Close < currentUpper.Value && last.Close < last.Open)
                    alerts.Add("Estilingue venda");
            }

            if (BollingerSqueezeBreakoutIndicator.IsConfirmed(candles, upper, lower, widthPercent, direction))
                alerts.Add(direction == TradeDirection.Long ? "EAB compra" : "EAB venda");
        }

        decimal range = last.High - last.Low;
        decimal body = Math.Abs(last.Close - last.Open);
        bool forceBar = atr > 0 && range >= atr * 1.8m && body / range >= .65m && volumeSpike >= 1.5m;
        if (forceBar && ((direction == TradeDirection.Long && last.Close > last.Open) ||
                         (direction == TradeDirection.Short && last.Close < last.Open)))
            alerts.Add(direction == TradeDirection.Long ? "Ignição compra" : "Ignição venda");

        return alerts;
    }

    private static bool IsNarrow(IReadOnlyList<decimal?> widths, int index)
    {
        const int lookback = 60;
        if (index < lookback - 1 || widths.Count <= index || widths[index] is null)
            return false;

        var history = widths
            .Skip(index - lookback + 1)
            .Take(lookback)
            .Where(width => width.HasValue)
            .Select(width => width!.Value)
            .OrderBy(width => width)
            .ToList();
        if (history.Count < lookback)
            return false;

        int percentileIndex = (int)Math.Floor((history.Count - 1) * .20m);
        return widths[index]!.Value <= history[percentileIndex];
    }
}
