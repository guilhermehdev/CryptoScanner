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
    public AssetAnalysis Analyze(string symbol, List<Candle> candles, List<Candle> btcCandles, ScanProfile profile, RiskCalculationMode riskMode = RiskCalculationMode.SwingBased, List<Candle>? symbolDailyCandles = null, TradeDirection direction = TradeDirection.Long, bool useInvertedRsiMomentum = false)
    {
        var structure = AnalyzeStructure(candles);
        var trend = AnalyzeTrend(candles, structure, direction, useInvertedRsiMomentum);
        var volume = AnalyzeVolume(candles, direction);
        var candle = AnalyzeCandle(candles);

        // Bandas de Bollinger — só calculadas quando o modo realmente usa (Reversão de
        // Bollinger), pra não pagar esse custo em todos os outros modos que não precisam.
        (List<decimal?> Middle, List<decimal?> Upper, List<decimal?> Lower, List<decimal?> BandWidthPercent)? bollinger =
            riskMode == RiskCalculationMode.BollingerReversal
                ? BollingerBandsIndicator.Calculate(candles)
                : null;

        var risk = AnalyzeRisk(candles, trend.Close, trend.Atr, trend.Ema21, riskMode, trend.TrendStrengthScore, direction, bollinger, symbolDailyCandles);
        var setup = AnalyzeSetup(candles, trend, risk, structure, candle, volume, btcCandles, profile, direction, riskMode, bollinger);

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

        analysis.OpportunityScore = OpportunityScoreCalculator.Calculate(analysis, direction);
        var (previousScore, variation) = ScoreTracker.Update(symbol, analysis.OpportunityScore);
        analysis.PreviousScore = previousScore;
        analysis.ScoreVariation = variation;

        return analysis;
    }

    private static TrendAnalysis AnalyzeTrend(List<Candle> candles, StructureAnalysis structure, TradeDirection direction, bool useInvertedRsiMomentum = false)
    {
        decimal close = candles[^1].Close;

        // Mantém a série completa (não só [^1]) pra poder medir inclinação das EMAs
        // logo abaixo — presume que o índice da série bate com o de candles (mesmo
        // padrão já usado em todo o resto do arquivo com EmaIndicator.Calculate(...)[^1]).
        var ema21Series = EmaIndicator.Calculate(candles, 21);
        var ema50Series = EmaIndicator.Calculate(candles, 50);
        var ema200Series = EmaIndicator.Calculate(candles, 200);
        decimal ema21 = ema21Series[^1] ?? 0;
        decimal ema50 = ema50Series[^1] ?? 0;
        decimal ema200 = ema200Series[^1] ?? 0;

        var rsiSeries = RsiIndicator.Calculate(candles);
        decimal rsi = rsiSeries[^1] ?? 0;
        decimal atr = AtrIndicator.Calculate(candles);
        decimal atrPercent = close > 0 ? atr / close * 100m : 0;

        // Fase A do lado de venda — alinhamento baixista das 3 EMAs. "Preço < EMA200" sozinho
        // é simplista demais (fica sobrevendido por muito tempo em tendência forte, sem
        // distinguir "baixa real" de "só cruzou a linha uma vez") — exige a ordem completa
        // (Preço < EMA21 < EMA50 < EMA200) E as 3 caindo nos últimos N candles, não só
        // alinhadas num instante isolado. Long não usa isso — segue sem essa exigência,
        // como sempre foi.
        const int emaSlopeLookback = 10;
        bool isBearishEmaAligned = close < ema21 && ema21 < ema50 && ema50 < ema200;
        bool isBearishEmaSloping = false;
        if (candles.Count > emaSlopeLookback)
        {
            decimal? pastEma21 = ema21Series[^(emaSlopeLookback + 1)];
            decimal? pastEma50 = ema50Series[^(emaSlopeLookback + 1)];
            decimal? pastEma200 = ema200Series[^(emaSlopeLookback + 1)];
            isBearishEmaSloping =
                pastEma21.HasValue && pastEma50.HasValue && pastEma200.HasValue &&
                ema21 < pastEma21.Value && ema50 < pastEma50.Value && ema200 < pastEma200.Value;
        }
        bool isBearishTrendConfirmed = isBearishEmaAligned && isBearishEmaSloping;

        // RSI — momentum e divergência (confirmação, não portão obrigatório; ver nota no
        // EligibilityEvaluator). Cruza o RSI exatamente nos mesmos candles onde o preço
        // formou seus dois últimos topos (índices já calculados no MarketStructureAnalyzer,
        // pra não duplicar a lógica de detecção de pivô aqui).
        //
        // Diagnóstico (12/2026): hadSwingHighDataAvailable captura só o pré-requisito de
        // índice válido, ANTES de qualquer checagem de RSI — separa "MarketStructureAnalyzer
        // não achou 2 swing highs/lows na janela" (dado indisponível, sai cedo no guard
        // swingHighs.Count<2||swingLows.Count<2) de "dado disponível mas Momentum/Divergência
        // genuinamente não ocorreram". Investigação disparada por 63/63 trades de Bollinger
        // Reversal (Short) terem vindo com os dois campos abaixo sempre false.
        bool hadSwingHighDataAvailable = structure.LastSwingHighIndex >= 0 && structure.PrevSwingHighIndex >= 0;

        bool isBearishMomentumConfirmed = false;
        bool isBearishRsiDivergence = false;
        if (structure.LastSwingHighIndex >= 0 && structure.PrevSwingHighIndex >= 0 &&
            structure.LastSwingHighIndex < rsiSeries.Count && structure.PrevSwingHighIndex < rsiSeries.Count)
        {
            decimal? rsiAtLastHigh = rsiSeries[structure.LastSwingHighIndex];
            decimal? rsiAtPrevHigh = rsiSeries[structure.PrevSwingHighIndex];
            if (rsiAtLastHigh.HasValue && rsiAtPrevHigh.HasValue)
            {
                bool rsiLowerHigh = rsiAtLastHigh.Value < rsiAtPrevHigh.Value;
                // Preço com topo mais baixo E RSI também com topo mais baixo — força
                // murchando junto com a estrutura (confirmação simples).
                isBearishMomentumConfirmed = structure.HasLowerHigh && rsiLowerHigh;
                // Preço com topo MAIS ALTO mas RSI com topo mais baixo — divergência clássica,
                // o sinal mais forte dos dois (força enfraquecendo apesar do preço subir).
                isBearishRsiDivergence = structure.HasHigherHigh && rsiLowerHigh;
            }
        }

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
            Score = direction == TradeDirection.Long
                ? TrendScorer.Calculate(close, ema21, ema50, ema200)
                : TrendScorer.CalculateBearish(close, ema21, ema50, ema200),
            MomentumScore = direction == TradeDirection.Long
                ? (useInvertedRsiMomentum ? MomentumScorer.CalculateInvertedRsi(rsi) : MomentumScorer.Calculate(rsi))
                : MomentumScorer.CalculateBearish(rsi),
            VolatilityScore = VolatilityScorer.Calculate(atrPercent), // direção-neutro, só magnitude
            TrendStrengthScore = direction == TradeDirection.Long
                ? TrendStrengthScorer.Calculate(close, ema21, ema50, ema200)
                : TrendStrengthScorer.CalculateBearish(close, ema21, ema50, ema200),
            Direction = structure.IsUptrend ? "ALTA" : structure.IsDowntrend ? "BAIXA" : "LATERAL",
            IsBearishTrendConfirmed = isBearishTrendConfirmed,
            IsBearishMomentumConfirmed = isBearishMomentumConfirmed,
            IsBearishRsiDivergence = isBearishRsiDivergence,
            HadSwingHighDataAvailable = hadSwingHighDataAvailable
        };
    }

    private static VolumeAnalysis AnalyzeVolume(List<Candle> candles, TradeDirection direction)
    {
        var result = VolumeAnalyzer.Calculate(candles);
        return new VolumeAnalysis
        {
            RelativeVolume = RelativeVolumeIndicator.Calculate(candles),
            BuyingVolume = result.BuyingVolume,
            SellingVolume = result.SellingVolume,
            Imbalance = result.VolumeImbalance,
            Spike = result.VolumeSpike,
            Score = direction == TradeDirection.Long ? result.Score : result.BearishScore,
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
            HasBearishBreakOfStructure = result.BearishBreakOfStructure,
            HasBearishChangeOfCharacter = result.BearishChangeOfCharacter,
            HasHigherHigh = result.HigherHigh,
            HasLowerHigh = result.LowerHigh,
            LastSwingHighIndex = result.LastSwingHighIndex,
            PrevSwingHighIndex = result.PrevSwingHighIndex,
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
        VolumeAnalysis volume,
        List<Candle> btcCandles,
        ScanProfile profile,
        TradeDirection direction,
        RiskCalculationMode mode,
        (List<decimal?> Middle, List<decimal?> Upper, List<decimal?> Lower, List<decimal?> BandWidthPercent)? bollinger)
    {
        decimal swingLow = candles.Skip(Math.Max(0, candles.Count - 20)).Min(c => c.Low);
        decimal swingHigh = candles.Skip(Math.Max(0, candles.Count - 20)).Max(c => c.High);
        var result = direction == TradeDirection.Long
            ? SetupQualityAnalyzer.Calculate(trend.Close, trend.Ema21, trend.Atr, swingLow)
            : SetupQualityAnalyzer.CalculateBearish(trend.Close, trend.Ema21, trend.Atr, swingHigh);
        decimal shortTermResistance = SupportResistanceIndicator.GetResistance(candles, profile.DefensiveBreakoutLookback);
        decimal shortTermSupport = SupportResistanceIndicator.GetSupport(candles, profile.DefensiveBreakoutLookback);

        // Fase A do lado de venda: preço fechando abaixo do suporte, sozinho, não é mais
        // suficiente — precisa também de confirmação estrutural real (sequência Lower High/
        // Lower Low culminando em rompimento, não só qualquer fechamento abaixo de uma linha).
        // Long continua exatamente como antes (não mexido), validado extensivamente nessa sessão.
        bool isBreakout = direction == TradeDirection.Long
            ? BreakoutIndicator.IsBullishBreakout(candles, risk.Resistance)
            : BreakoutIndicator.IsBearishBreakout(candles, risk.Support)
                && (structure.HasBearishBreakOfStructure || structure.HasBearishChangeOfCharacter);
        bool isShortTermBreakout = direction == TradeDirection.Long
            ? BreakoutIndicator.IsBullishBreakout(candles, shortTermResistance)
            : BreakoutIndicator.IsBearishBreakout(candles, shortTermSupport)
                && (structure.HasBearishBreakOfStructure || structure.HasBearishChangeOfCharacter);

        // Caminho A — repique: tendência de alta já estabelecida, com sinal de virada no candle atual.
        // Long apenas — caminho adicional construído e validado só pro lado de compra;
        // estender pra venda fica pra uma fase futura, se a venda clássica validar bem.
        bool isPullbackBounce =
            direction == TradeDirection.Long &&
            structure.IsUptrend &&
            (candle.IsBullishEngulfing || candle.IsHammer || structure.LiquiditySweepLow);

        // Reversão à média (Scalp) — Long apenas, mesma justificativa do Caminho A acima.
        bool isMeanReversionSetup =
            direction == TradeDirection.Long &&
            structure.IsUptrend &&
            trend.Atr > 0 &&
            (trend.Ema21 - trend.Close) / trend.Atr >= 1.0m &&
            (candle.IsBullishEngulfing || candle.IsHammer || structure.LiquiditySweepLow);

        // Reversão de Bollinger (Fase A do lado de venda) — banda superior + resistência
        // como ZONA DE GATILHO (não fechamento obrigatório acima, nem alvo — o alvo é a
        // volta pra banda média). Exige rejeição confirmada e um filtro contra "andar na
        // banda": se a força compradora ainda está clara e forte, não vale a pena brigar
        // contra ela só porque tocou a banda superior.
        bool isBollingerReversalSetup = false;
        if (direction == TradeDirection.Short && mode == RiskCalculationMode.BollingerReversal &&
            bollinger.HasValue && trend.Atr > 0)
        {
            decimal? currentUpper = bollinger.Value.Upper[^1];
            if (currentUpper.HasValue)
            {
                // Zona de proximidade — chute inicial, não validado, sujeito a calibração
                // via comparador (igual RR/Distância foram calibrados nos outros modos).
                const decimal proximityAtr = 1.0m;

                decimal swingResistance = SupportResistanceIndicator.GetResistance(candles);
                bool isNearUpperBand = Math.Abs(currentUpper.Value - trend.Close) / trend.Atr <= proximityAtr;
                bool isNearResistance = Math.Abs(swingResistance - trend.Close) / trend.Atr <= proximityAtr;

                // Rejeição: pavio superior longo (BuyerRejection), Engolfo de baixa, ou
                // Estrela Cadente — os 3 já existentes no CandleAnalysis, sem precisar de
                // detecção nova.
                bool hasRejection = candle.HasBuyerRejection || candle.IsBearishEngulfing || candle.IsShootingStar;

                // "Andar na banda": usa as versões de ALTA dos scorers, independente da
                // direção sendo testada — aqui a pergunta é "a força compradora está forte
                // demais pra brigar", não "qual a força na direção que estou testando"
                // (que já seria a versão baixista, calculada em trend.Score/MomentumScore
                // pra esse teste). Limiares são chute inicial, a calibrar.
                decimal bullishTrendStrength = TrendStrengthScorer.Calculate(trend.Close, trend.Ema21, trend.Ema50, trend.Ema200);
                int bullishMomentum = MomentumScorer.Calculate(trend.Rsi);
                bool isWalkingTheBand =
                    bullishTrendStrength >= 25m && // teste diagnóstico — bem permissivo, só pra confirmar se dispara
                    bullishMomentum >= 40 &&
                    volume.Imbalance > 0.05m;

                isBollingerReversalSetup = isNearUpperBand && isNearResistance && hasRejection && !isWalkingTheBand;
            }
        }

        // Caminho de RSI baixo (Fase 3, 16/08/2026) — ver comentário completo em
        // SetupAnalysis.cs. Mesmo pré-requisito de tendência do Caminho A (structure.
        // IsUptrend), mais RSI<45 e candle não fortemente vendedor (evita entrar em cima
        // de uma vela de rejeição clara só porque o RSI está baixo).
        bool isLowRsiSetup =
            direction == TradeDirection.Long &&
            structure.IsUptrend &&
            trend.Rsi < 45m &&
            !candle.IsStrongBearish;

        return new SetupAnalysis
        {
            Score = result.Score,
            IsBreakout = isBreakout,
            IsShortTermBreakout = isShortTermBreakout,
            RelativeStrength = RelativeStrengthIndicator.Calculate(candles, btcCandles, ScannerSettings.RelativeStrengthPeriodHours),
            IsConsolidating = ConsolidationIndicator.IsConsolidating(candles),
            IsOverextended = result.IsOverextended,
            EmaDistanceAtr = result.EmaDistanceAtr,
            SwingUsageAtr = result.SwingUsageAtr,
            IsPullbackBounce = isPullbackBounce,
            IsMeanReversionSetup = isMeanReversionSetup,
            IsBollingerReversalSetup = isBollingerReversalSetup,
            IsLowRsiSetup = isLowRsiSetup
        };
    }

    private static RiskAnalysis AnalyzeRisk(List<Candle> candles, decimal close, decimal atr, decimal ema21, RiskCalculationMode mode, int trendStrengthScore, TradeDirection direction,
        (List<decimal?> Middle, List<decimal?> Upper, List<decimal?> Lower, List<decimal?> BandWidthPercent)? bollinger = null,
        List<Candle>? symbolDailyCandles = null)
    {
        if (mode == RiskCalculationMode.BollingerReversal && direction == TradeDirection.Short && bollinger.HasValue)
        {
            decimal? currentMiddle = bollinger.Value.Middle[^1];
            if (currentMiddle.HasValue)
            {
                // Stop: resistência estrutural + buffer de ATR — múltiplo é chute inicial,
                // o próprio usuário sugeriu testar 0,3/0,5/0,7 via comparador antes de fixar.
                const decimal stopAtrBuffer = 0.5m; // ponto de equilíbrio entre os 3 testados (0,3/0,5/0,7)

                decimal swingResistance = SupportResistanceIndicator.GetResistance(candles);
                decimal stop = swingResistance + (atr * stopAtrBuffer);

                // V1: só TP1 (banda média) — fechamento único. TP2 (suporte estrutural) e
                // TP3 (próximo suporte/projeção) ficam pra quando a engine de saída parcial
                // (StrategyBacktester.ProcessPartialExits) reconhecer direção — hoje ela só
                // sabe operar Long (checa High pra alvo, Low pra stop, sempre). Popular
                // TakeProfit1/3 aqui sem essa correção calcularia errado silenciosamente.
                decimal takeProfit = currentMiddle.Value;
                decimal resistanceDistance = (stop - close) / close * 100m; // aqui, "Resistance" = stop (acima do preço)
                decimal supportDistance = (close - takeProfit) / close * 100m; // aqui, "Support" = alvo (abaixo do preço)

                return new RiskAnalysis
                {
                    Resistance = stop,
                    Support = takeProfit,
                    ResistanceDistancePercent = resistanceDistance,
                    SupportDistancePercent = supportDistance,
                    RiskReward = resistanceDistance > 0 ? supportDistance / resistanceDistance : 0,
                    Mode = RiskCalculationMode.BollingerReversal
                };
            }
        }

        if (mode == RiskCalculationMode.MeanReversionScalp)
        {
            // Alvo: volta pra EMA21 — deliberadamente próximo (ver motivação no comentário
            // de isMeanReversionSetup, em AnalyzeSetup). O stop PRECISA ser proporcionalmente
            // próximo também — usar o suporte estrutural distante dos outros modos geraria
            // RR estruturalmente baixo (alvo perto ÷ stop longe), quase sempre abaixo de
            // qualquer piso razoável. Por isso, múltiplo de ATR pequeno, no mesmo espírito
            // do modo AtrBased (onde alvo e stop são os dois múltiplos de ATR, garantindo
            // uma proporção sensata por construção). Multiplicador é um chute inicial, não
            // validado — sujeito a calibração via comparador, como RR/Distância nos outros modos.
            const decimal meanReversionStopAtrMultiplier = 0.75m;

            decimal resistance = ema21;
            decimal support = close - (atr * meanReversionStopAtrMultiplier);
            decimal resistanceDistance = (resistance - close) / close * 100m;
            decimal supportDistance = (close - support) / close * 100m;

            return new RiskAnalysis
            {
                Resistance = resistance,
                Support = support,
                ResistanceDistancePercent = resistanceDistance,
                SupportDistancePercent = supportDistance,
                RiskReward = direction == TradeDirection.Long
                    ? (supportDistance > 0 ? resistanceDistance / supportDistance : 0)
                    : (resistanceDistance > 0 ? supportDistance / resistanceDistance : 0),
                Mode = RiskCalculationMode.MeanReversionScalp
            };
        }

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
                RiskReward = direction == TradeDirection.Long
                    ? (supportDistance > 0 ? resistanceDistance / supportDistance : 0)
                    : (resistanceDistance > 0 ? supportDistance / resistanceDistance : 0),
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

            // Escada de saída parcial (TP1/TP2/TP3) pressupõe alvo ACIMA do preço — só faz
            // sentido pra Long. Pra Short, fica de fora (Fase 1 do lado de venda não estende
            // a saída parcial ainda) — TakeProfit1/3 ficam null, e o motor cai sozinho no
            // fechamento único de sempre, usando Resistance/Support normalmente.
            decimal? takeProfit1 = null;
            decimal? takeProfit3 = null;

            if (direction == TradeDirection.Long)
            {
                // TP1: proporcional ao TP2 (60% do caminho), nunca fixo em 2R — evita a escada
                // ficar fora de ordem quando o RR real é menor que 2 (comum na faixa RR≈1,5-1,7
                // que validamos como a melhor pra esse modo).
                takeProfit1 = close + (resistance - close) * 0.60m;

                // TP3: segunda resistência estrutural real, se o scanner achou uma; senão,
                // extensão de Fibonacci adaptativa pela força de tendência (ADX como proxy).
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
            }

            return new RiskAnalysis
            {
                Resistance = resistance,
                Support = bufferedSupport,
                ResistanceDistancePercent = resistanceDistance,
                SupportDistancePercent = supportDistance,
                RiskReward = direction == TradeDirection.Long
                    ? (supportDistance > 0 ? resistanceDistance / supportDistance : 0)
                    : (resistanceDistance > 0 ? supportDistance / resistanceDistance : 0),
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
                RiskReward = direction == TradeDirection.Long
                    ? (supportDistance > 0 ? resistanceDistance / supportDistance : 0)
                    : (resistanceDistance > 0 ? supportDistance / resistanceDistance : 0),
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
            RiskReward = direction == TradeDirection.Long
                ? (originalSupportDistance > 0 ? originalResistanceDistance / originalSupportDistance : 0)
                : (originalResistanceDistance > 0 ? originalSupportDistance / originalResistanceDistance : 0),
            Mode = RiskCalculationMode.SwingBased
        };
    }
}