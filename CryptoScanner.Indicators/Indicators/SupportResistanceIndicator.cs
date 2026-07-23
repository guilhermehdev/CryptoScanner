using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class SupportResistanceIndicator
    {
        public static decimal GetResistance(List<Candle> candles, int lookback = 50)
        {
            var recent = candles.TakeLast(lookback);

            return recent.Max(x => x.High);
        }

        public static decimal GetSupport(List<Candle> candles, int lookback = 50)
        {
            var recent = candles.TakeLast(lookback);

            return recent.Min(x => x.Low);
        }
    }
}
