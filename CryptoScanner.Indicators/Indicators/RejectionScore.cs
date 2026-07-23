using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class RejectionScore
    {
        public static decimal Calculate(List<Candle> candles)
        {
            var recent = candles.TakeLast(3);

            decimal maxScore = 0;

            foreach (var candle in recent)
            {              

                decimal upperWick = candle.High - Math.Max(candle.Open, candle.Close);
                decimal candleRange = candle.High - candle.Low;

                if (candleRange == 0)
                    continue;

                decimal score = upperWick / candleRange;

                if (score > maxScore)
                    maxScore = score;
            }

            return maxScore;
        }
    }
}
