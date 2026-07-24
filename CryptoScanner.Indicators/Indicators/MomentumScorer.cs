using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class MomentumScorer
    {
        public static int Calculate(decimal rsi)
        {
            if (rsi > 80)
                return 30; // muito esticado, risco de exaustão

            if (rsi > 70)
                return 50; // sobrecomprado

            if (rsi >= 55)
                return 100; // faixa ideal de momentum

            if (rsi >= 50)
                return 80;

            if (rsi >= 45)
                return 60;

            if (rsi >= 40)
                return 40;

            return 20;
        }
    }
}
