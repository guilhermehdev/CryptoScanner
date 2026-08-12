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

        // Espelho pro lado de baixa (Fase A do lado de venda) — reflexo em torno de 50.
        // Faixa ideal de momentum baixista fica em 30-45 (equivalente a 55-70 da alta).
        public static int CalculateBearish(decimal rsi)
        {
            if (rsi < 20)
                return 30; // muito esticado pra baixo, risco de exaustão
            if (rsi < 30)
                return 50; // sobrevendido
            if (rsi <= 45)
                return 100; // faixa ideal de momentum baixista
            if (rsi <= 50)
                return 80;
            if (rsi <= 55)
                return 60;
            if (rsi < 60)
                return 40;
            return 20;
        }
    }
}