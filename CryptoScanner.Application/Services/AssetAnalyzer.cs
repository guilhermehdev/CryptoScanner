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
    public AssetAnalysis Analyze(string symbol, List<Candle> candles, List<Candle> btcCandles, ScanProfile profile, RiskCalculationMode riskMode = RiskCalculationMode.SwingBased, List<Candle>? symbolDailyCandles = null)
    {
        var structure = AnalyzeStructure(candles);
        var trend = AnalyzeTrend(candles, structure);
        var volume = AnalyzeVolume(candles);
        var candle = AnalyzeCandle(candles);
        var risk = AnalyzeRisk(candles, trend.Close, trend.Atr, riskMode, trend.TrendStrengthScore, symbolDailyCandles);
        var setup = AnalyzeSetup(candles, trend, risk, structure, candle, btcCandles, profile);

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
            HasDistribution = result.Distribution,
            HasExhaustion = result.Exhaustion
        };
    }

    private static StructureAnalysis AnalyzeStructure(List<Candle> candles)
    {
        var result = MarketStructureAnalyzer.Calculate(candles);
        var smartMoney = SmartMoneyAnalyzer.Calculate(candles);

        int score = Math.Clamp(result.Score + smartMoney.Bonus, 0, 100);

        return new StructureAnalysis
        {
            Score = score,
            IsUptrend = result.Uptrend,
            IsDowntrend = result.Downtrend,
            IsStrongUptrend = result.StrongUptrend,
            IsStrongDowntrend = result.StrongDowntrend,
            HasBreakOfStructure = result.BreakOfStructure,
            HasChangeOfCharacter = result.ChangeOfCharacter,
            LiquiditySweepHigh = smartMoney.LiquiditySweepHigh,
            LiquiditySweepLow = smartMoney.LiquiditySweepLow,
            IsBullTrap = smartMoney.IsBullTrap,
            IsBearTrap = smartMoney.IsBearTrap,
            SmartMoneyLabel = smartMoney.Label
        };
    }

    private static CandleAnalysis AnalyzeCandle(List<Candle> candles)
    {
        var result = CandleQualityAnalyzer.Calculate(candles);
        var pattern = CandlePatternDetector.Calculate(candles);

        int score = Math.Clamp(result.Score + pattern.Bonus, 0, 100);

        return new CandleAnalysis
        {
            Score = score,
            BullPower = result.BullPower,
            BearPower = result.BearPower,
            BodyRatio = result.BodyRatio,
            UpperWickRatio = result.UpperWickRatio,
            LowerWickRatio = result.LowerWickRatio,
            IsStrongBullish = result.StrongBullish,
            IsStrongBearish = result.StrongBearish,
            HasBuyerRejection = result.BuyerRejection,
            HasSellerRejection = result.SellerRejection,
            RejectionScore = RejectionScore.Calculate(candles),
            IsDoji = pattern.IsDoji,
            IsHammer = pattern.IsHammer,
            IsShootingStar = pattern.IsShootingStar,
            IsBullishMarubozu = pattern.IsBullishMarubozu,
            IsBearishMarubozu = pattern.IsBearishMarubozu,
            IsBullishEngulfing = pattern.IsBullishEngulfing,
            IsBearishEngulfing = pattern.IsBearishEngulfing,
            PatternName = pattern.PatternName
        };
    }

    private static SetupAnalysis AnalyzeSetup(
        List<Candle> candles,
        TrendAnalysis trend,
        RiskAnalysis risk,
        StructureAnalysis structure,
        CandleAnalysis candle,
        List<Candle> btcCandles,
        ScanProfile profile)
    {
        decimal swingLow = candles.Skip(Math.Max(0, candles.Count - 20)).Min(c => c.Low);
        var result = SetupQualityAnalyzer.Calculate(trend.Close, trend.Ema21, trend.Atr, swingLow);

        decimal shortTermResistance = SupportResistanceIndicator.GetResistance(candles, profile.DefensiveBreakoutLookback);

        // Caminho A — repique: tendência de alta já estabelecida, com sinal de virada no candle atual.
        bool isPullbackBounce =
            structure.IsUptrend &&
            (candle.IsBullishEngulfing || candle.IsHammer || structure.LiquiditySweepLow);

        return new SetupAnalysis
        {
            Score = result.Score,
            IsBreakout = BreakoutIndicator.IsBullishBreakout(candles, risk.Resistance),
            IsShortTermBreakout = BreakoutIndicator.IsBullishBreakout(candles, shortTermResistance),
            RelativeStrength = RelativeStrengthIndicator.Calculate(candles, btcCandles, ScannerSettings.RelativeStrengthPeriodHours),
            IsConsolidating = ConsolidationIndicator.IsConsolidating(candles),
            IsOverextended = result.IsOverextended,
            EmaDistanceAtr = result.EmaDistanceAtr,
            SwingUsageAtr = result.SwingUsageAtr,
            IsPullbackBounce = isPullbackBounce
        };
    }

    private static RiskAnalysis AnalyzeRisk(List<Candle> candles, decimal close, decimal atr, RiskCalculationMode mode, int trendStrengthScore, List<Candle>? symbolDailyCandles = null)
    {
        if (mode == RiskCalculationMode.AtrBased)
        {
            decimal resistance = close + (atr * ScannerSettings.AtrTargetMultiplier);
            decimal support = close - (atr * ScannerSettings.AtrStopMultiplier);
            decimal resistanceDistance = (resistance - close) / close * 100m;
            decimal supportDistance = (close - support) / close * 100m;

            return new RiskAnalysis
            {
                Resistance = resistance,
                Support = support,
                ResistanceDistancePercent = resistanceDistance,
                SupportDistancePercent = supportDistance,
                RiskReward = supportDistance > 0 ? resistanceDistance / supportDistance : 0,
                Mode = RiskCalculationMode.AtrBased
            };
        }

        if (mode == RiskCalculationMode.SwingWithPartialExits)
        {
            decimal swingSupport = SupportResistanceIndicator.GetSupport(candles);
            decimal bufferedSupport = swingSupport - (atr * ScannerSettings.AtrBufferMultiplier);

            var zones = ResistanceScanner.ScanMultiTimeframe(candles, symbolDailyCandles, close, atr); // etapa 4.2
            decimal resistance = zones.Count > 0
                ? zones[0].Price
                : SupportResistanceIndicator.GetResistance(candles);

            decimal resistanceDistance = (resistance - close) / close * 100m;
            decimal supportDistance = (close - bufferedSupport) / close * 100m;

            // TP1: proporcional ao TP2 (60% do caminho), nunca fixo em 2R — evita a escada
            // ficar fora de ordem quando o RR real é menor que 2 (comum na faixa RR≈1,5-1,7
            // que validamos como a melhor pra esse modo).
            decimal takeProfit1 = close + (resistance - close) * 0.60m;

            // TP3: segunda resistência estrutural real, se o scanner achou uma; senão,
            // extensão de Fibonacci adaptativa pela força de tendência (ADX como proxy).
            decimal takeProfit3;
            if (zones.Count > 1)
            {
                takeProfit3 = zones[1].Price;
            }
            else
            {
                // V1: extensão de Fibonacci por faixas do TrendStrengthScore (0/25/50/75/100),
                // em vez de ADX — mais alinhado com a proposta original, mesmo sendo um score
                // simples (só mede distância à EMA200). V2/V3 (score composto, função contínua)
                // ficam registrados como refinamento futuro.
                decimal fibExtension = trendStrengthScore switch
                {
                    <= 25 => 1.272m,
                    <= 75 => 1.618m,
                    _ => 2.618m
                };
                takeProfit3 = close + (resistance - close) * fibExtension;
            }

            return new RiskAnalysis
            {
                Resistance = resistance,
                Support = bufferedSupport,
                ResistanceDistancePercent = resistanceDistance,
                SupportDistancePercent = supportDistance,
                RiskReward = supportDistance > 0 ? resistanceDistance / supportDistance : 0,
                Mode = RiskCalculationMode.SwingWithPartialExits,
                TakeProfit1 = takeProfit1,
                TakeProfit3 = takeProfit3
            };
        }

        if (mode == RiskCalculationMode.SwingWithAtrBuffer)
        {
            decimal swingResistance = SupportResistanceIndicator.GetResistance(candles);
            decimal swingSupport = SupportResistanceIndicator.GetSupport(candles);

            // Alvo continua sendo a resistência estrutural real — só o stop ganha a folga extra.
            decimal bufferedSupport = swingSupport - (atr * ScannerSettings.AtrBufferMultiplier);

            decimal resistanceDistance = (swingResistance - close) / close * 100m;
            decimal supportDistance = (close - bufferedSupport) / close * 100m;

            return new RiskAnalysis
            {
                Resistance = swingResistance,
                Support = bufferedSupport,
                ResistanceDistancePercent = resistanceDistance,
                SupportDistancePercent = supportDistance,
                RiskReward = supportDistance > 0 ? resistanceDistance / supportDistance : 0,
                Mode = RiskCalculationMode.SwingWithAtrBuffer
            };
        }

       

        // Comportamento original (padrão do app ao vivo hoje) — inalterado.
        decimal originalResistance = SupportResistanceIndicator.GetResistance(candles);
        decimal originalSupport = SupportResistanceIndicator.GetSupport(candles);
        decimal originalResistanceDistance = (originalResistance - close) / close * 100m;
        decimal originalSupportDistance = (close - originalSupport) / close * 100m;

        return new RiskAnalysis
        {
            Resistance = originalResistance,
            Support = originalSupport,
            ResistanceDistancePercent = originalResistanceDistance,
            SupportDistancePercent = originalSupportDistance,
            RiskReward = originalSupportDistance > 0 ? originalResistanceDistance / originalSupportDistance : 0,
            Mode = RiskCalculationMode.SwingBased
        };
    }
}