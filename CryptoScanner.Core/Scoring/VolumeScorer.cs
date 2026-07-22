using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Scoring;

public static class VolumeScorer
{
    public static int Calculate(decimal relativeVolume, decimal volumeSpike, bool breakout)
    {
        int score = 0;

        if (relativeVolume >= 1.5m)
            score += 30;

        if (volumeSpike >= 2m)
            score += 40;

        if (breakout)
            score += 30;

        return score;
    }
}