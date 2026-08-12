using System;
using System.Collections.Generic;
using System.Text;
using CryptoScanner.Core.Models;
using System;

namespace CryptoScanner.Core.Scoring
{
    public class SetupQualityResult
    {
        public int Score { get; set; }
        public bool IsOverextended { get; set; }
        public decimal EmaDistanceAtr { get; set; }
        public decimal SwingUsageAtr { get; set; }
    }

    public static class SetupQualityAnalyzer
    {
        public static SetupQualityResult Calculate(decimal currentPrice, decimal ema21, decimal atr, decimal swingLow, decimal maxEmaDistAtr = 1.5m, decimal maxSwingAtr = 2.0m)
        {
            if (atr == 0 || ema21 == 0)
                return new SetupQualityResult { Score = 0 };

            // 1. Distância da EMA21 em múltiplos de ATR
            decimal distEmaAbs = currentPrice - ema21;
            decimal distEmaAtr = distEmaAbs / atr;

            // 2. Uso do movimento (exaustão desde o último fundo)
            decimal swingMoveAbs = currentPrice - swingLow;
            decimal swingUsageAtr = swingMoveAbs / atr;

            decimal score = 100m;
            bool isLate = false;

            // Penalidade: Longe da EMA
            if (distEmaAtr > maxEmaDistAtr)
            {
                decimal penalty = Math.Min((distEmaAtr - maxEmaDistAtr) * 20m, 40m);
                score -= penalty;
                isLate = true;
            }

            // Penalidade: Já esticou demais no Swing
            if (swingUsageAtr > maxSwingAtr)
            {
                decimal penalty = Math.Min((swingUsageAtr - maxSwingAtr) * 15m, 30m);
                score -= penalty;
            }

            // Bônus: Timing perfeito / Penalidade: Contra a EMA
            if (distEmaAtr > 0 && distEmaAtr <= 0.5m && swingUsageAtr <= 1.0m)
            {
                score += 5;
            }
            else if (distEmaAtr < 0)
            {
                score -= 30;
            }

            return new SetupQualityResult
            {
                Score = (int)Math.Clamp(Math.Round(score, 0), 0, 100),
                IsOverextended = isLate || (swingUsageAtr > maxSwingAtr),
                EmaDistanceAtr = distEmaAtr,
                SwingUsageAtr = swingUsageAtr
            };
        }

        // Espelho pro lado de baixa (Fase A do lado de venda) — distância ABAIXO da EMA21
        // (não acima), e "uso do movimento" desde o último TOPO (não fundo) recente. Mesma
        // lógica de penalidade/bônus, só os pontos de referência trocam de lado.
        public static SetupQualityResult CalculateBearish(decimal currentPrice, decimal ema21, decimal atr, decimal swingHigh, decimal maxEmaDistAtr = 1.5m, decimal maxSwingAtr = 2.0m)
        {
            if (atr == 0 || ema21 == 0)
                return new SetupQualityResult { Score = 0 };

            // 1. Distância da EMA21 em múltiplos de ATR — positiva quando o preço está
            // ABAIXO da EMA21 (contexto de venda favorável), espelho do cálculo original.
            decimal distEmaAbs = ema21 - currentPrice;
            decimal distEmaAtr = distEmaAbs / atr;

            // 2. Uso do movimento desde o último topo — positivo quando o preço já caiu
            // bastante desde lá (espelho de "desde o último fundo" na versão de alta).
            decimal swingMoveAbs = swingHigh - currentPrice;
            decimal swingUsageAtr = swingMoveAbs / atr;

            decimal score = 100m;
            bool isLate = false;

            if (distEmaAtr > maxEmaDistAtr)
            {
                decimal penalty = Math.Min((distEmaAtr - maxEmaDistAtr) * 20m, 40m);
                score -= penalty;
                isLate = true;
            }

            if (swingUsageAtr > maxSwingAtr)
            {
                decimal penalty = Math.Min((swingUsageAtr - maxSwingAtr) * 15m, 30m);
                score -= penalty;
            }

            if (distEmaAtr > 0 && distEmaAtr <= 0.5m && swingUsageAtr <= 1.0m)
            {
                score += 5;
            }
            else if (distEmaAtr < 0)
            {
                score -= 30;
            }

            return new SetupQualityResult
            {
                Score = (int)Math.Clamp(Math.Round(score, 0), 0, 100),
                IsOverextended = isLate || (swingUsageAtr > maxSwingAtr),
                EmaDistanceAtr = distEmaAtr,
                SwingUsageAtr = swingUsageAtr
            };
        }
    }
}