using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class MarketRegimeIndicator
    {
        private const decimal SidewaysBandPercent = 3m;

        public static string Calculate(decimal btcPrice, decimal btcEma200)
        {
            if (btcEma200 <= 0)
                return "LATERAL";

            decimal distancePercent = ((btcPrice - btcEma200) / btcEma200) * 100m;

            if (Math.Abs(distancePercent) <= SidewaysBandPercent)
                return "LATERAL";

            return distancePercent > 0 ? "BULL" : "BEAR";
        }
    }
}