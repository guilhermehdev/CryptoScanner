using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Scoring;

public static class TrendStrengthScorer
{
    public static int Calculate(decimal close, decimal ema21, decimal ema50, decimal ema200)
    {
        if (ema200 <= 0)
            return 0;

        decimal distance = ((close - ema200) / ema200) * 100;

        if (distance < 0)
            return 0;

        if (distance < 5)
            return 25;

        if (distance < 10)
            return 50;

        if (distance < 20)
            return 75;

        return 100;
    }
}