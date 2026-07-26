using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoScanner.Indicators.Indicators
{
    public class SmartMoneyResult
    {
        public bool LiquiditySweepHigh { get; set; }
        public bool LiquiditySweepLow { get; set; }
        public bool IsBullTrap { get; set; }
        public bool IsBearTrap { get; set; }
        public int Bonus { get; set; }

        public string Label =>
            IsBullTrap ? "BULL_Trap" :
            IsBearTrap ? "BEAR_Trap" :
            LiquiditySweepHigh ? "FLUSH (Alta)" :
            LiquiditySweepLow ? "FLUSH (Baixa)" :
            "";
    }

    public static class SmartMoneyAnalyzer
    {
        public static SmartMoneyResult Calculate(List<Candle> candles)
        {
            var result = new SmartMoneyResult();

            if (candles.Count < 60)
                return result;

            result.LiquiditySweepHigh = DetectLiquiditySweepHigh(candles);
            result.LiquiditySweepLow = DetectLiquiditySweepLow(candles);
            result.IsBullTrap = DetectBullTrap(candles);
            result.IsBearTrap = DetectBearTrap(candles);

            result.Bonus = CalculateBonus(result);

            return result;
        }

        // Mecha ultrapassa máxima recente, mas fecha de volta abaixo dela no mesmo candle.
        private static bool DetectLiquiditySweepHigh(List<Candle> candles)
        {
            const int swingLookback = 10;
            const int recentWindow = 3;

            decimal priorSwingHigh = candles
                .Skip(Math.Max(0, candles.Count - swingLookback - recentWindow))
                .Take(swingLookback)
                .Max(c => c.High);

            return candles.TakeLast(recentWindow)
                .Any(c => c.High > priorSwingHigh && c.Close < priorSwingHigh);
        }

        // Mecha ultrapassa mínima recente, mas fecha de volta acima dela no mesmo candle.
        private static bool DetectLiquiditySweepLow(List<Candle> candles)
        {
            const int swingLookback = 10;
            const int recentWindow = 3;

            decimal priorSwingLow = candles
                .Skip(Math.Max(0, candles.Count - swingLookback - recentWindow))
                .Take(swingLookback)
                .Min(c => c.Low);

            return candles.TakeLast(recentWindow)
                .Any(c => c.Low < priorSwingLow && c.Close > priorSwingLow);
        }

        // Algum candle recente rompeu acima da resistência anterior, mas o fechamento
        // atual já está de volta abaixo dela — rompimento que não se sustentou.
        private static bool DetectBullTrap(List<Candle> candles)
        {
            const int lookback = 50;
            const int recentWindow = 10;

            decimal priorResistance = candles
                .Skip(Math.Max(0, candles.Count - lookback - recentWindow))
                .Take(lookback)
                .Max(c => c.High);

            bool brokeAbove = candles.TakeLast(recentWindow).Any(c => c.High > priorResistance);
            bool closedBackBelow = candles[^1].Close < priorResistance;

            return brokeAbove && closedBackBelow;
        }

        // Algum candle recente rompeu abaixo do suporte anterior, mas o fechamento
        // atual já está de volta acima dele — rompimento pra baixo que não segurou.
        private static bool DetectBearTrap(List<Candle> candles)
        {
            const int lookback = 50;
            const int recentWindow = 10;

            decimal priorSupport = candles
                .Skip(Math.Max(0, candles.Count - lookback - recentWindow))
                .Take(lookback)
                .Min(c => c.Low);

            bool brokeBelow = candles.TakeLast(recentWindow).Any(c => c.Low < priorSupport);
            bool closedBackAbove = candles[^1].Close > priorSupport;

            return brokeBelow && closedBackAbove;
        }

        private static int CalculateBonus(SmartMoneyResult result)
        {
            int bonus = 0;

            if (result.IsBullTrap) bonus -= 15;
            if (result.IsBearTrap) bonus += 15;
            if (result.LiquiditySweepHigh) bonus -= 12;
            if (result.LiquiditySweepLow) bonus += 12;

            return bonus;
        }
    }
}