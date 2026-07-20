using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class RelativeVolumeIndicator
{
    public static decimal Calculate(
        List<Candle> candles,
        int period = 20)
    {
        if (candles.Count < period + 1)
            return 1;

        decimal currentVolume =
            candles.Last().Volume;

        decimal averageVolume =
            candles
                .Skip(candles.Count - period - 1)
                .Take(period)
                .Average(x => x.Volume);

        if (averageVolume == 0)
            return 1;

        return currentVolume / averageVolume;
    }
}
