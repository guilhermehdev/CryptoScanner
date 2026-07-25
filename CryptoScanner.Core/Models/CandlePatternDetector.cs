using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;

namespace CryptoScanner.Indicators.Indicators
{
    public class CandlePatternResult
    {
        public bool IsDoji { get; set; }
        public bool IsHammer { get; set; }
        public bool IsShootingStar { get; set; }
        public bool IsBullishMarubozu { get; set; }
        public bool IsBearishMarubozu { get; set; }
        public bool IsBullishEngulfing { get; set; }
        public bool IsBearishEngulfing { get; set; }
        public int Bonus { get; set; }

        public string PatternName =>
            IsBullishEngulfing ? "Engolfo Alta" :
            IsBearishEngulfing ? "Engolfo Baixa" :
            IsBullishMarubozu ? "Marubozu Alta" :
            IsBearishMarubozu ? "Marubozu Baixa" :
            IsHammer ? "Martelo" :
            IsShootingStar ? "Estrela Cadente" :
            IsDoji ? "Doji" :
            "";
    }

    public static class CandlePatternDetector
    {
        public static CandlePatternResult Calculate(List<Candle> candles)
        {
            var result = new CandlePatternResult();

            if (candles.Count == 0)
                return result;

            Candle last = candles[^1];

            decimal body = Math.Abs(last.Close - last.Open);
            decimal range = last.High - last.Low;
            decimal upperWick = last.High - Math.Max(last.Open, last.Close);
            decimal lowerWick = Math.Min(last.Open, last.Close) - last.Low;

            if (range == 0)
                return result;

            decimal bodyRatio = body / range;

            result.IsDoji = bodyRatio < 0.10m;

            result.IsHammer =
                !result.IsDoji &&
                lowerWick >= body * 2m &&
                upperWick <= body * 0.5m &&
                bodyRatio < 0.40m;

            result.IsShootingStar =
                !result.IsDoji &&
                upperWick >= body * 2m &&
                lowerWick <= body * 0.5m &&
                bodyRatio < 0.40m;

            result.IsBullishMarubozu =
                last.Close > last.Open &&
                bodyRatio > 0.90m;

            result.IsBearishMarubozu =
                last.Close < last.Open &&
                bodyRatio > 0.90m;

            if (candles.Count >= 2)
            {
                Candle prev = candles[^2];

                bool prevBearish = prev.Close < prev.Open;
                bool prevBullish = prev.Close > prev.Open;
                bool lastBullish = last.Close > last.Open;
                bool lastBearish = last.Close < last.Open;

                // Corpo do candle atual precisa "engolir" completamente o corpo do anterior.
                result.IsBullishEngulfing =
                    prevBearish &&
                    lastBullish &&
                    last.Open <= prev.Close &&
                    last.Close >= prev.Open;

                result.IsBearishEngulfing =
                    prevBullish &&
                    lastBearish &&
                    last.Open >= prev.Close &&
                    last.Close <= prev.Open;
            }

            result.Bonus = CalculateBonus(result);

            return result;
        }

        private static int CalculateBonus(CandlePatternResult result)
        {
            if (result.IsBullishEngulfing) return 15;
            if (result.IsBearishEngulfing) return -15;
            if (result.IsBullishMarubozu) return 10;
            if (result.IsBearishMarubozu) return -10;
            if (result.IsHammer) return 8;
            if (result.IsShootingStar) return -8;
            return 0; // Doji = indecisão, não soma nem penaliza
        }
    }
}