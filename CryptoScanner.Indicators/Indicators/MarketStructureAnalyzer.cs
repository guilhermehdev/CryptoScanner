using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class MarketStructureAnalyzer
{
    public static MarketStructureResult Calculate(List<Candle> candles)
    {
        MarketStructureResult result = new();

        if (candles.Count < 50)
            return result;

        List<Candle> swingHighs = new();
        List<Candle> swingLows = new();

        for (int i = 2; i < candles.Count - 2; i++)
        {
            bool isHigh =
                candles[i].High > candles[i - 1].High &&
                candles[i].High > candles[i - 2].High &&
                candles[i].High > candles[i + 1].High &&
                candles[i].High > candles[i + 2].High;

            bool isLow =
                candles[i].Low < candles[i - 1].Low &&
                candles[i].Low < candles[i - 2].Low &&
                candles[i].Low < candles[i + 1].Low &&
                candles[i].Low < candles[i + 2].Low;

            if (isHigh)
                swingHighs.Add(candles[i]);

            if (isLow)
                swingLows.Add(candles[i]);
        }

        result.SwingHighCount = swingHighs.Count;
        result.SwingLowCount = swingLows.Count;

        if (swingHighs.Count < 2 || swingLows.Count < 2)
        {
            result.Score = 50;
            result.Sideways = true;
            return result;
        }

        Candle lastHigh = swingHighs[^1];
        Candle prevHigh = swingHighs[^2];

        Candle lastLow = swingLows[^1];
        Candle prevLow = swingLows[^2];

        result.HigherHigh = lastHigh.High > prevHigh.High;
        result.HigherLow = lastLow.Low > prevLow.Low;

        result.LowerHigh = lastHigh.High < prevHigh.High;
        result.LowerLow = lastLow.Low < prevLow.Low;

        if (result.HigherHigh && result.HigherLow)
        {
            result.Uptrend = true;
            result.Score = 100;
        }
        else if (result.LowerHigh && result.LowerLow)
        {
            result.Downtrend = true;
            result.Score = 0;
        }
        else
        {
            result.Sideways = true;
            result.Score = 50;
        }

        decimal close = candles[^1].Close;

        result.BreakOfStructure =
            close > prevHigh.High;

        result.ChangeOfCharacter =
            result.BreakOfStructure &&
            result.HigherLow;

        result.StrongUptrend =
            result.Uptrend &&
            result.BreakOfStructure &&
            result.HigherHigh &&
            result.HigherLow;

        result.StrongDowntrend =
            result.Downtrend &&
            result.LowerHigh &&
            result.LowerLow &&
            close < prevLow.Low;

        if (result.StrongUptrend)
            result.Score = 100;
        else if (result.Uptrend)
            result.Score = 85;
        else if (result.Sideways)
            result.Score = 50;
        else if (result.Downtrend)
            result.Score = 15;
        else
            result.Score = 0;

        return result;
    }
}