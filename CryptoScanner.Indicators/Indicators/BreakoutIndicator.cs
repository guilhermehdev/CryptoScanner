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

    // Espelho de IsBullishBreakout — rompimento de suporte pra venda (Fase 1 do lado de venda).
    public static bool IsBearishBreakout(List<Candle> candles, decimal support)
    {
        if (candles.Count == 0)
            return false;

        decimal currentClose = candles.Last().Close;

        return currentClose < support;
    }
}