using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Scoring;

public static class MomentumScorer
{
    public static int Calculate(decimal rsi, decimal atrPercent, decimal adx)
    {
        int score = 0;

        if (rsi >= 55 && rsi <= 70)
            score += 30;

        if (atrPercent >= 2m)
            score += 30;

        if (adx >= 25)
            score += 40;

        return score;
    }
}
