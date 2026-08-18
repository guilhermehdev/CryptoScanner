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

        // Variante experimental (16/08/2026) — Fase 3 do roadmap (Aprendizado). Descoberta
        // via análise por fator numa amostra de 3.885 trades (Long, limiares soltos pra
        // exploração, SwingWithPartialExits): RSI baixo na entrada teve PF melhor (RSI<30:
        // PF 1,75, Retorno médio +1,79%) que RSI alto (RSI>=70: PF 0,89, Retorno médio
        // -0,66%) — padrão praticamente monotônico, com amostra >80 trades em cada faixa.
        // É o OPOSTO do que Calculate() acima premia (nota máxima em RSI 55-70). Essa
        // variante inverte a curva pra testar se isso melhora a config real validada.
        // Ainda não validado — só testável via checkbox "Testar Momentum RSI invertido"
        // no BacktestWindow (chkInvertedMomentum), Long apenas. Como MomentumScore pesa só
        // 5% do OpportunityScore total (ScannerSettings.MomentumWeight), o efeito esperado
        // é sutil — se não for suficiente, o próximo passo é testar como filtro de
        // elegibilidade direto (mesmo padrão do RequireBearishMomentumConfirmed).
        public static int CalculateInvertedRsi(decimal rsi)
        {
            if (rsi < 30)
                return 100; // melhor faixa observada (PF 1,75)
            if (rsi < 45)
                return 80; // PF 1,35
            if (rsi < 55)
                return 60; // PF 1,25
            if (rsi < 70)
                return 40; // PF 1,08
            return 20; // pior faixa observada (PF 0,89, retorno médio negativo)
        }
    }
}