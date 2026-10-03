using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

/// <summary>
/// Detecta EAB: contração da largura das Bandas de Bollinger seguida de expansão e
/// fechamento além da banda. É um gatilho de pesquisa, não um sinal ao vivo.
/// </summary>
public static class BollingerSqueezeBreakoutIndicator
{
    private const int WidthLookback = 60;
    private const decimal SqueezePercentile = 20m;

    public static bool IsConfirmed(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<decimal?> upper,
        IReadOnlyList<decimal?> lower,
        IReadOnlyList<decimal?> widthPercent,
        TradeDirection direction)
    {
        int current = candles.Count - 1;
        int previous = current - 1;
        if (candles.Count < WidthLookback + 20 || previous < 0 ||
            upper.Count <= current || lower.Count <= current || widthPercent.Count <= current ||
            upper[current] is null || lower[current] is null || widthPercent[current] is null || widthPercent[previous] is null)
            return false;

        var history = widthPercent
            .Skip(Math.Max(0, previous - WidthLookback + 1))
            .Take(WidthLookback)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToList();
        if (history.Count < WidthLookback)
            return false;

        int percentileIndex = (int)Math.Floor((history.Count - 1) * (SqueezePercentile / 100m));
        decimal squeezeCeiling = history[percentileIndex];
        bool wasSqueezed = widthPercent[previous]!.Value <= squeezeCeiling;
        bool isExpanding = widthPercent[current]!.Value > widthPercent[previous]!.Value;
        bool closesOutside = direction == TradeDirection.Long
            ? candles[current].Close > upper[current]!.Value
            : candles[current].Close < lower[current]!.Value;

        return wasSqueezed && isExpanding && closesOutside;
    }
}
