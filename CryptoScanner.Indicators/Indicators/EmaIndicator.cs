using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class EmaIndicator
{
    public static List<decimal?> Calculate(
        List<Candle> candles,
        int period)
    {
        var result = new List<decimal?>();

        decimal multiplier = 2m / (period + 1);

        decimal? ema = null;

        for (int i = 0; i < candles.Count; i++)
        {
            decimal close = candles[i].Close;

            if (i < period - 1)
            {
                result.Add(null);
                continue;
            }

            if (ema == null)
            {
                ema = candles
                    .Skip(i - period + 1)
                    .Take(period)
                    .Average(x => x.Close);
            }
            else
            {
                ema = ((close - ema.Value) * multiplier) + ema.Value;
            }

            result.Add(ema);
        }

        return result;
    }
}
