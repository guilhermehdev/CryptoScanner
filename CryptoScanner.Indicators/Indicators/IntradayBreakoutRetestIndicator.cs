using CryptoScanner.Core.Models;
using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Indicators.Indicators;

/// <summary>
/// Detecta o padrão de rompimento + reteste em candles já fechados.
/// O penúltimo candle precisa fechar além do nível de 20 candles anteriores;
/// o último candle retesta o nível e fecha novamente na direção do rompimento.
/// </summary>
public static class IntradayBreakoutRetestIndicator
{
    public static bool IsConfirmed(List<Candle> candles, decimal atr, TradeDirection direction, int lookback = 20)
    {
        if (atr <= 0 || candles.Count < lookback + 2)
            return false;

        var baseCandles = candles.SkipLast(2).TakeLast(lookback).ToList();
        var breakout = candles[^2];
        var retest = candles[^1];

        if (direction == TradeDirection.Long)
        {
            decimal resistance = baseCandles.Max(c => c.High);
            decimal retestUpperBound = resistance + atr * 0.50m;
            return breakout.Close > resistance &&
                   retest.Close > resistance &&
                   retest.Close > retest.Open &&
                   retest.Low <= retestUpperBound;
        }

        decimal support = baseCandles.Min(c => c.Low);
        decimal retestLowerBound = support - atr * 0.50m;
        return breakout.Close < support &&
               retest.Close < support &&
               retest.Close < retest.Open &&
               retest.High >= retestLowerBound;
    }
}
