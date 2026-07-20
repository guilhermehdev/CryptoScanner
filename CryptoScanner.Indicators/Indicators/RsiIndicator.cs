using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class RsiIndicator
{
    public static List<decimal?> Calculate(
        List<Candle> candles,
        int period = 14)
    {
        var result = new List<decimal?>();

        for (int i = 0; i < candles.Count; i++)
        {
            if (i < period)
            {
                result.Add(null);
                continue;
            }

            decimal gain = 0;
            decimal loss = 0;

            for (int j = i - period + 1; j <= i; j++)
            {
                decimal diff =
                    candles[j].Close -
                    candles[j - 1].Close;

                if (diff > 0)
                    gain += diff;
                else
                    loss += Math.Abs(diff);
            }

            decimal avgGain =
                gain / period;

            decimal avgLoss =
                loss / period;

            if (avgLoss == 0)
            {
                result.Add(100);
                continue;
            }

            decimal rs =
                avgGain / avgLoss;

            decimal rsi =
                100 - (100 / (1 + rs));

            result.Add(rsi);
        }

        return result;
    }
}