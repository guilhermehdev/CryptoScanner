using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators;

public static class AdxIndicator
{
    public static decimal Calculate(List<Candle> candles, int period = 14)
    {
        if (candles.Count < period + 1)
            return 0;

        decimal trSum = 0;
        decimal plusDmSum = 0;
        decimal minusDmSum = 0;

        for (int i = candles.Count - period; i < candles.Count; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];

            decimal highDiff = current.High - previous.High;
            decimal lowDiff = previous.Low - current.Low;

            decimal plusDm = (highDiff > lowDiff && highDiff > 0) ? highDiff : 0;
            decimal minusDm = (lowDiff > highDiff && lowDiff > 0) ? lowDiff : 0;

            decimal tr = (decimal)Math.Max((double)(current.High - current.Low), Math.Max((double)Math.Abs(current.High - previous.Close), (double)Math.Abs(current.Low - previous.Close)));

            trSum += (decimal)tr;
            plusDmSum += plusDm;
            minusDmSum += minusDm;
        }

        if (trSum == 0)
            return 0;

        decimal plusDi = (plusDmSum / trSum) * 100;
        decimal minusDi = (minusDmSum / trSum) * 100;

        if ((plusDi + minusDi) == 0)
            return 0;

        decimal dx = (Math.Abs(plusDi - minusDi) / (plusDi + minusDi)) * 100;

        return Math.Round(dx, 2);
    }
}