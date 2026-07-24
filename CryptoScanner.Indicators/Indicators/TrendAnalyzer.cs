using CryptoScanner.Core.Models;
using CryptoScanner.Core.Scoring;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class TrendAnalyzer
    {
        public static TrendAnalysisResult Calculate(List<Candle> candles)
        {
            TrendAnalysisResult result = new();

            decimal close = candles.Last().Close;

            decimal ema21 = EmaIndicator.Calculate(candles, 21).Last() ?? 0;
            decimal ema50 = EmaIndicator.Calculate(candles, 50).Last() ?? 0;
            decimal ema200 = EmaIndicator.Calculate(candles, 200).Last() ?? 0;

            decimal rsi = RsiIndicator.Calculate(candles).Last() ?? 0;

            decimal atr = AtrIndicator.Calculate(candles);

            decimal atrPercent = 0;

            if (close > 0)
                atrPercent = (atr / close) * 100m;

            decimal adx = AdxIndicator.Calculate(candles);

            result.Close = close;
            result.Ema21 = ema21;
            result.Ema50 = ema50;
            result.Ema200 = ema200;
            result.Rsi = rsi;
            result.Atr = atr;
            result.AtrPercent = atrPercent;
            result.Adx = adx;

            int trendScore = 0;

            if (close > ema200)
                trendScore += 30;

            if (ema21 > ema50)
                trendScore += 25;

            if (ema50 > ema200)
                trendScore += 25;

            if (close > ema21)
                trendScore += 20;

            result.TrendScore = trendScore;

            result.MomentumScore = MomentumScorer.Calculate(rsi);

            result.VolatilityScore = VolatilityScorer.Calculate(atrPercent);

            result.TrendStrengthScore = TrendStrengthScorer.Calculate(close, ema21, ema50, ema200);

            if (close > ema200 && ema21 > ema50)
                result.TrendDirection = "ALTA";
            else if (close < ema200 && ema21 < ema50)
                result.TrendDirection = "BAIXA";

            return result;
        }
    }
}
