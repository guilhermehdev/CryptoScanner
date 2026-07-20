using System;
using System.Collections.Generic;
using System.Text;

using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators;

public static class BreakoutIndicator
{
    public static bool IsBullishBreakout(
        List<Candle> candles,
        int lookback = 20)
    {
        if (candles.Count < lookback + 1)
            return false;

        decimal resistance =
            candles
                .Skip(candles.Count - lookback - 1)
                .Take(lookback)
                .Max(x => x.High);

        decimal currentClose =
            candles.Last().Close;

        return currentClose > resistance;
    }

    public static decimal GetResistance(
        List<Candle> candles,
        int lookback = 20)
    {
        if (candles.Count < lookback)
            return 0;

        return candles
            .Skip(candles.Count - lookback)
            .Max(x => x.High);
    }
}