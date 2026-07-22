using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class ConsolidationIndicator
    {
        public static bool IsConsolidating(List<Candle> candles, int lookback = 20, decimal maxRangePercent = 5m)
        {
            if (candles.Count < lookback)
                return false;

            var recent = candles.TakeLast(lookback).ToList();

            decimal highest = recent.Max(x => x.High);
            decimal lowest = recent.Min(x => x.Low);

            if (lowest <= 0)
                return false;

            decimal rangePercent = ((highest - lowest) / lowest) * 100;

            return rangePercent <= maxRangePercent;
        }
    }
}
