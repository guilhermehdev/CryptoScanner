using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Scoring;
using CryptoScanner.Core.Services;
using CryptoScanner.Indicators;
using CryptoScanner.Indicators.Indicators;
using CryptoScanner.Strategies;

namespace CryptoScanner.Application.Services;

public sealed class AssetAnalyzer
{
    public AssetAnalysis Analyze(string symbol, List<Candle> candles, List<Candle> btcCandles)
    {
        var structure = AnalyzeStructure(candles);
        var trend = AnalyzeTrend(candles, structure);
        var volume = AnalyzeVolume(candles);
        var candle = AnalyzeCandle(candles);
        var risk = AnalyzeRisk(candles, trend.Close);
        var setup = AnalyzeSetup(candles, trend, risk.Resistance, btcCandles);

        var analysis = new AssetAnalysis
        {
            Symbol = symbol,
            Trend = trend,
            Volume = volume,
            Structure = structure,
            Risk = risk,
            Candle = candle,
            Setup = setup
        };

        analysis.OpportunityScore = OpportunityScoreCalculator.Calculate(analysis);

        var (previousScore, variation) = ScoreTracker.Update(symbol, analysis.OpportunityScore);
        analysis.PreviousScore = previousScore;
        analysis.ScoreVariation = variation;

        return analysis;
    }

    private static TrendAnalysis AnalyzeTrend(List<Candle> candles, StructureAnalysis structure)
    {
        decimal close = candles[^1].Close;
        decimal ema21 = EmaIndicator.Calculate(candles, 21)[^1] ?? 0;
        decimal ema50 = EmaIndicator.Calculate(candles, 50)[^1] ?? 0;
        decimal ema200 = EmaIndicator.Calculate(candles, 200)[^1] ?? 0;
        decimal rsi = RsiIndicator.Calculate(candles)[^1] ?? 0;
        decimal atr = AtrIndicator.Calculate(candles);
        decimal atrPercent = close > 0 ? atr / close * 100m : 0;

        return new TrendAnalysis
        {
            Close = close,
            Ema21 = ema21,
            Ema50 = ema50,
            Ema200 = ema200,
            Rsi = rsi,
            Atr = atr,
            AtrPercent = atrPercent,
            Adx = AdxIndicator.Calculate(candles),
            Score = TrendScorer.Calculate(close, ema21, ema50, ema200),
            MomentumScore = MomentumScorer.Calculate(rsi),
            VolatilityScore = VolatilityScorer.Calculate(atrPercent),
            TrendStrengthScore = TrendStrengthScorer.Calculate(close, ema21, ema50, ema200),
            Direction = structure.IsUptrend ? "ALTA" : structure.IsDowntrend ? "BAIXA" : "LATERAL"
        };
    }

    private static VolumeAnalysis AnalyzeVolume(List<Candle> candles)
    {
        var result = VolumeAnalyzer.Calculate(candles);
        return new VolumeAnalysis
        {
            RelativeVolume = RelativeVolumeIndicator.Calculate(candles),
            BuyingVolume = result.BuyingVolume,
            SellingVolume = result.SellingVolume,
            Imbalance = result.VolumeImbalance,
            Spike = result.VolumeSpike,
            Score = result.Score,
            IsClimax = result.ClimaxVolume,
            HasAbsorption = result.Absorption,
            HasDistribution = result.Distribution
        };
    }

    private static StructureAnalysis AnalyzeStructure(List<Candle> candles)
    {
        var result = MarketStructureAnalyzer.Calculate(candles);
        return new StructureAnalysis
        {
            Score = result.Score,
            IsUptrend = result.Uptrend,
            IsDowntrend = result.Downtrend,
            IsStrongUptrend = result.StrongUptrend,
            IsStrongDowntrend = result.StrongDowntrend,
            HasBreakOfStructure = result.BreakOfStructure,
            HasChangeOfCharacter = result.ChangeOfCharacter
        };
    }

    private static CandleAnalysis AnalyzeCandle(List<Candle> candles)
    {
        var result = CandleQualityAnalyzer.Calculate(candles);
        return new CandleAnalysis
        {
            Score = result.Score,
            BullPower = result.BullPower,
            BearPower = result.BearPower,
            BodyRatio = result.BodyRatio,
            UpperWickRatio = result.UpperWickRatio,
            LowerWickRatio = result.LowerWickRatio,
            IsStrongBullish = result.StrongBullish,
            IsStrongBearish = result.StrongBearish,
            HasBuyerRejection = result.BuyerRejection,
            HasSellerRejection = result.SellerRejection,
            RejectionScore = RejectionScore.Calculate(candles)
        };
    }

    private static SetupAnalysis AnalyzeSetup(List<Candle> candles, TrendAnalysis trend, decimal resistance, List<Candle> btcCandles)
    {
        decimal swingLow = candles.Skip(Math.Max(0, candles.Count - 20)).Min(candle => candle.Low);
        var result = SetupQualityAnalyzer.Calculate(trend.Close, trend.Ema21, trend.Atr, swingLow);

        decimal shortTermResistance = SupportResistanceIndicator.GetResistance(candles, ScannerSettings.DefensiveBreakoutLookback);

        return new SetupAnalysis
        {
            Score = result.Score,
            IsBreakout = BreakoutIndicator.IsBullishBreakout(candles, resistance),
            IsShortTermBreakout = BreakoutIndicator.IsBullishBreakout(candles, shortTermResistance),
            RelativeStrength = RelativeStrengthIndicator.Calculate(candles, btcCandles, ScannerSettings.RelativeStrengthPeriodHours),
            IsConsolidating = ConsolidationIndicator.IsConsolidating(candles),
            IsOverextended = result.IsOverextended,
            EmaDistanceAtr = result.EmaDistanceAtr,
            SwingUsageAtr = result.SwingUsageAtr
        };
    }

    private static RiskAnalysis AnalyzeRisk(List<Candle> candles, decimal close)
    {
        decimal resistance = SupportResistanceIndicator.GetResistance(candles);
        decimal support = SupportResistanceIndicator.GetSupport(candles);
        decimal resistanceDistance = (resistance - close) / close * 100m;
        decimal supportDistance = (close - support) / close * 100m;

        return new RiskAnalysis
        {
            Resistance = resistance,
            Support = support,
            ResistanceDistancePercent = resistanceDistance,
            SupportDistancePercent = supportDistance,
            RiskReward = supportDistance > 0 ? resistanceDistance / supportDistance : 0
        };
    }
}