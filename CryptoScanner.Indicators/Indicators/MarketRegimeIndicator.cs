using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class MarketRegimeIndicator
    {
        public static string Calculate(decimal btcPrice, decimal btcEma200)
        {
            if (btcPrice > btcEma200)
                return "BULL";

            return "BEAR";
        }
    }
}
