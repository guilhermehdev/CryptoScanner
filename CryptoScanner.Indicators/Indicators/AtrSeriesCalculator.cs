using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class AtrSeriesCalculator
{
    /// <summary>
    /// Calcula a série completa de ATR (suavização de Wilder) pra todos os candles de
    /// uma vez. Não reaproveita o AtrIndicator existente porque ele só retorna o valor
    /// mais recente — chamá-lo repetidamente candle a candle voltaria a ser O(n²), o
    /// mesmo problema de performance que já corrigimos no motor de backtest.
    /// </summary>
    public static List<decimal?> Calculate(List<Candle> candles, int period = 14)
    {
        int count = candles.Count;
        var atr = new List<decimal?>(new decimal?[count]);

        if (count < period + 1)
            return atr;

        var trueRanges = new List<decimal>(count) { candles[0].High - candles[0].Low };

        for (int i = 1; i < count; i++)
        {
            decimal highLow = candles[i].High - candles[i].Low;
            decimal highPrevClose = Math.Abs(candles[i].High - candles[i - 1].Close);
            decimal lowPrevClose = Math.Abs(candles[i].Low - candles[i - 1].Close);
            trueRanges.Add(Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose)));
        }

        decimal seed = 0;
        for (int i = 0; i < period; i++)
            seed += trueRanges[i];
        seed /= period;

        atr[period - 1] = seed;

        decimal previous = seed;
        for (int i = period; i < count; i++)
        {
            decimal current = ((previous * (period - 1)) + trueRanges[i]) / period;
            atr[i] = current;
            previous = current;
        }

        return atr;
    }
}