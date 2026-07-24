using System;
using System.Collections.Generic;
using System.Text;

using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators;

public static class BreakoutIndicator
{
    public static bool IsBullishBreakout(List<Candle> candles, decimal resistance)
    {
        if (candles.Count == 0)
            return false;

        decimal currentClose = candles.Last().Close;

        return currentClose > resistance;
    }
}