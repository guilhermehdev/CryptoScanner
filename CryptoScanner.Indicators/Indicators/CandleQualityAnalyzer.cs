using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    public static class CandleQualityAnalyzer
    {
        public static CandleQualityResult Calculate(List<Candle> candles)
        {
            CandleQualityResult result = new();

            if (candles.Count == 0)
                return result;

            Candle candle = candles.Last();

            decimal body = Math.Abs(candle.Close - candle.Open);
            decimal upperWick = candle.High - Math.Max(candle.Open, candle.Close);
            decimal lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
            decimal range = candle.High - candle.Low;

            if (range == 0)
                return result;

            result.BodyRatio = body / range;
            result.UpperWickRatio = upperWick / range;
            result.LowerWickRatio = lowerWick / range;

            result.BullPower = (result.BodyRatio + result.LowerWickRatio) * 100m;
            result.BearPower = (result.BodyRatio + result.UpperWickRatio) * 100m;

            result.StrongBullish =
                candle.Close > candle.Open &&
                result.BodyRatio > 0.60m;

            result.StrongBearish =
                candle.Close < candle.Open &&
                result.BodyRatio > 0.60m;

            result.BuyerRejection =
                result.UpperWickRatio > 0.45m;

            result.SellerRejection =
                result.LowerWickRatio > 0.45m;

            result.Score = 50;

            if (result.StrongBullish)
                result.Score += 25;

            if (result.StrongBearish)
                result.Score -= 25;

            if (result.BuyerRejection)
                result.Score -= 20;

            if (result.SellerRejection)
                result.Score += 15;

            result.Score = Math.Clamp(result.Score, 0, 100);

            return result;
        }
    }
}
