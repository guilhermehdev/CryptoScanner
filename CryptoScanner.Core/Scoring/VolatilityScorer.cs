using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Scoring;

public static class VolatilityScorer
{
    public static int Calculate(
        decimal atrPercent)
    {
        if (atrPercent < 1)
            return 20;

        if (atrPercent < 2)
            return 50;

        if (atrPercent < 4)
            return 80;

        return 100;
    }
}
