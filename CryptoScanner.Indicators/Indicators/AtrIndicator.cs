using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class AtrIndicator
{
    public static decimal Calculate(List<Candle> candles, int period = 14)
    {
        if (candles.Count < period + 1)
            return 0;

        List<decimal> trueRanges = new();

        for (int i = 1; i < candles.Count; i++)
        {
            decimal high = candles[i].High;
            decimal low = candles[i].Low;
            decimal previousClose = candles[i - 1].Close;

            decimal tr1 = high - low;

            decimal tr2 =
                Math.Abs(high - previousClose);

            decimal tr3 =
                Math.Abs(low - previousClose);

            decimal tr =
                Math.Max(
                    tr1,
                    Math.Max(tr2, tr3));

            trueRanges.Add(tr);
        }

        return trueRanges
            .TakeLast(period)
            .Average();
    }
}