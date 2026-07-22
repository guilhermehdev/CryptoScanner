using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Scoring;

public static class MarketStructureScorer
{
    public static int Calculate(
        decimal close,
        decimal ema21,
        decimal ema50,
        decimal ema200)
    {
        int score = 0;

        if (close > ema21)
            score += 20;

        if (close > ema50)
            score += 20;

        if (close > ema200)
            score += 20;

        if (ema21 > ema50)
            score += 20;

        if (ema50 > ema200)
            score += 20;

        return score;
    }
}