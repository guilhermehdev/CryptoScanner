using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
       public static class VolumeSpikeIndicator
    {
        public static decimal Calculate(List<Candle> candles, int period = 20)
        {
            if (candles.Count < period + 1)
                return 0;

            decimal currentVolume = candles.Last().Volume;

            decimal averageVolume =
                candles
                .Skip(candles.Count - period - 1)
                .Take(period)
                .Average(x => x.Volume);

            if (averageVolume == 0)
                return 0;

            return currentVolume / averageVolume;
        }
    }
}
