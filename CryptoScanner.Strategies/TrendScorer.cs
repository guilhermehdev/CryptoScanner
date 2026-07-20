using System;
using System.Collections.Generic;
using System.Text;

using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

public static class TrendScorer
{
    public static int Calculate(
     decimal close,
     decimal ema21,
     decimal ema50,
     decimal ema200,
     decimal rsi,
     decimal relativeVolume, decimal atrPercent, bool breakout)
    {
        int score = 0;      

        if (close > ema200)
            score += 20;

        if (ema21 > ema50)
            score += 15;

        if (ema50 > ema200)
            score += 15;

        if (rsi >= 50 && rsi <= 70)
            score += 15;

        if (relativeVolume >= 1.5m)
            score += 20;

        if (atrPercent >= 2m)
            score += 15;

        if (breakout && relativeVolume >= 1.5m)
            score += 30;

        return score;
    }
}