using CryptoScanner.Application.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Utilities;
using CryptoScanner.Indicators.Indicators;


namespace CryptoScanner.Application.Services;

public sealed class StrategyBacktester
{
    /// <summary>
    /// Incrementa sempre que uma mudança no motor (StrategyBacktester, AssetAnalyzer,
    /// ResistanceScanner, etc.) puder alterar o resultado de um teste já salvo. Isso invalida
    /// automaticamente a deduplicação do Histórico pra testes antigos — sem essa versão, uma
    /// configuração de tela idêntica gera a mesma assinatura de sempre, e o sistema recusa
    /// salvar o resultado novo mesmo que o motor por trás tenha mudado completamente.
    /// </summary>
    public const int EngineVersion = 6; // v6: teto de distância de stop (MaxStopDistancePercent) disponível como filtro

    private const int LookbackCandles = 300;
    private readonly IMarketDataService _marketData;
    private readonly AssetAnalyzer _assetAnalyzer;

    public StrategyBacktester(IMarketDataService marketData, AssetAnalyzer assetAnalyzer)
    {
        _marketData = marketData;
        _assetAnalyzer = assetAnalyzer;
    }

    private const int MaxConcurrentSymbols = 6;

    public async Task<BacktestSummary> RunAsync(
     IReadOnlyList<string> symbols,
     DateTime startUtc,
     DateTime endUtc,
     ScanProfile profile,
     EligibilityThresholds? thresholds = null,
     RiskCalculationMode riskMode = RiskCalculationMode.SwingBased,
     int? evaluationHoursOverride = null,
     (decimal Tp1, decimal Tp2)? partialExitFractions = null,
     bool disableTimeout = false,
     TradeDirection direction = TradeDirection.Long,
     Action<string, double>? onProgress = null,
     CancellationToken cancellationToken = default)
    {
        var intervalSpan = CandleIntervalHelper.ToTimeSpan(profile.CandleInterval);
        var fetchStart = startUtc - TimeSpan.FromTicks(intervalSpan.Ticks * LookbackCandles);

        onProgress?.Invoke("Buscando dados do BTC...", 0);
        var btcCandles = await _marketData.GetHistoricalCandlesAsync("BTCUSDT", profile.CandleInterval, fetchStart, endUtc, cancellationToken);

        var dailyFetchStart = startUtc - TimeSpan.FromDays(220);
        var btcDailyCandles = await _marketData.GetHistoricalCandlesAsync("BTCUSDT", "1d", dailyFetchStart, endUtc, cancellationToken);

        var allTrades = new List<BacktestTradeResult>();
        var diagnostics = new FilterDiagnostics();
        var skippedSymbols = new List<string>();
        var tradesLock = new object();
        var diagnosticsLock = new object();
        var progressLock = new object();
        int totalSymbols = symbols.Count;
        int completedSymbols = 0;

        using var throttle = new SemaphoreSlim(MaxConcurrentSymbols);
        var tasks = symbols.Select(async symbol =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<Candle> candles;
                List<Candle>? symbolDailyCandles = null;
                try
                {
                    candles = await _marketData.GetHistoricalCandlesAsync(symbol, profile.CandleInterval, fetchStart, endUtc, cancellationToken);

                    // Candles diários do próprio ativo — só busca quando o modo realmente usa
                    // E o checkbox "Multi-timeframe (4.2)" está marcado. A 4.1 pura continua
                    // funcionando sem isso — ScanMultiTimeframe cai de volta pro comportamento
                    // de timeframe único quando não recebe candles diários (symbolDailyCandles=null).
                    if (riskMode == RiskCalculationMode.SwingWithPartialExits && thresholds?.EnableMultiTimeframe == true)
                    {
                        symbolDailyCandles = await _marketData.GetHistoricalCandlesAsync(symbol, "1d", dailyFetchStart, endUtc, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    lock (tradesLock) { skippedSymbols.Add($"{symbol} (erro ao buscar dados: {ex.Message})"); }
                    return;
                }
                finally
                {
                    // Pausa por moeda individual, mesmo em paralelo — mitiga rate limit da Binance
                    // sem exigir que tudo rode em sequência única.
                    await Task.Delay(200, cancellationToken);
                }

                if (candles.Count < LookbackCandles)
                {
                    lock (tradesLock) { skippedSymbols.Add($"{symbol} (histórico insuficiente: {candles.Count} candles)"); }
                    return;
                }

                var (trades, symbolDiagnostics) = await Task.Run(
                    () => SimulateSymbol(symbol, candles, btcCandles, btcDailyCandles, symbolDailyCandles, startUtc, profile, thresholds, riskMode, evaluationHoursOverride, partialExitFractions, disableTimeout, direction,
                        (message, _) => { }),
                    cancellationToken);

                lock (tradesLock) { allTrades.AddRange(trades); }
                lock (diagnosticsLock) { MergeDiagnostics(diagnostics, symbolDiagnostics); }
            }
            finally
            {
                int done;
                lock (progressLock) { done = ++completedSymbols; }
                double overallPercent = totalSymbols > 0 ? done * 100.0 / totalSymbols : 0;
                onProgress?.Invoke($"Concluídas {done}/{totalSymbols} moedas...", overallPercent);
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        onProgress?.Invoke("Calculando resumo...", 100);
        return BuildSummary(allTrades, diagnostics, skippedSymbols);
    }

    private (List<BacktestTradeResult> Trades, FilterDiagnostics Diagnostics) SimulateSymbol(
        string symbol,
        List<Candle> candles,
        List<Candle> btcCandles,
        List<Candle> btcDailyCandles,
        List<Candle>? symbolDailyCandles,
        DateTime startUtc,
        ScanProfile profile,
        EligibilityThresholds? thresholds,
        RiskCalculationMode riskMode,
        int? evaluationHoursOverride,
        (decimal Tp1, decimal Tp2)? partialExitFractions,
        bool disableTimeout,
        TradeDirection direction,
        Action<string, double>? onProgress)
    {
        var trades = new List<BacktestTradeResult>();
        var diagnostics = new FilterDiagnostics();
        BacktestOpenPosition? openPosition = null;
        var lastSignalTimeByKey = new Dictionary<string, DateTime>();

        int startIndex = candles.FindIndex(c => c.OpenTime >= startUtc);
        if (startIndex < 0 || startIndex < LookbackCandles)
            startIndex = LookbackCandles;

        int totalToProcess = candles.Count - startIndex;
        const int progressReportInterval = 200;
        int skippedInsufficientData = 0;
        int effectiveEvaluationHours = evaluationHoursOverride ?? profile.EvaluationHours;

        // Ponteiros que só avançam — como as listas estão em ordem cronológica, dá pra
        // manter a janela dos últimos candles de cada uma sem reescanear do início a cada
        // iteração (o que tornava o teste quadrático em vez de linear).
        int btcIndex = 0;
        int btcDailyIndex = 0;
        int symbolDailyIndex = 0; // etapa 4.2 — multi-timeframe

        for (int i = startIndex; i < candles.Count; i++)
        {
            if ((i - startIndex) % progressReportInterval == 0)
            {
                int processed = i - startIndex;
                int percent = totalToProcess > 0 ? processed * 100 / totalToProcess : 0;
                onProgress?.Invoke($"Testando {symbol}: {percent}% ({processed}/{totalToProcess} candles)...", percent);
            }

            var currentCandle = candles[i];
            bool justClosed = false;

            if (openPosition != null)
            {
                if (openPosition.TakeProfit1.HasValue)
                {
                    // Modo SwingWithPartialExits (etapa 4.3) — saída fracionada TP1→TP2→TP3.
                    // Long apenas — TakeProfit1 nunca é preenchido pra Short (ver AssetAnalyzer).
                    justClosed = ProcessPartialExits(openPosition, currentCandle, trades);
                    if (!disableTimeout && !justClosed && currentCandle.OpenTime >= openPosition.EntryTime.AddHours(effectiveEvaluationHours))
                    {
                        // Timeout com posição parcialmente realizada — fecha só o que sobrou,
                        // ponderado junto com as pernas de TP1/TP2 já realizadas.
                        decimal legReturn = (currentCandle.Close - openPosition.EntryPrice) / openPosition.EntryPrice * 100m;
                        openPosition.WeightedExitSum += openPosition.RemainingFraction * legReturn;
                        string reason = openPosition.Tp1Hit
                            ? (openPosition.Tp2Hit ? "TP1TP2TIMEOUT" : "TP1TIMEOUT")
                            : "TIMEOUT";
                        trades.Add(CloseTradeWeighted(openPosition, currentCandle.OpenTime, reason));
                        justClosed = true;
                    }

                    if (justClosed)
                        openPosition = null;
                }
                else
                {
                    // Fase 1 do lado de venda: Long checa Low<=Stop / High>=Alvo (comportamento
                    // original, inalterado); Short espelha — o stop fica ACIMA do preço de
                    // entrada, o alvo fica ABAIXO, então as checagens de High/Low se invertem.
                    if (openPosition.Direction == TradeDirection.Long)
                    {
                        if (currentCandle.Low <= openPosition.StopLoss)
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.StopLoss, "SL"));
                            openPosition = null;
                            justClosed = true;
                        }
                        else if (currentCandle.High >= openPosition.TakeProfit)
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.TakeProfit, "TP"));
                            openPosition = null;
                            justClosed = true;
                        }
                        else if (!disableTimeout && currentCandle.OpenTime >= openPosition.EntryTime.AddHours(effectiveEvaluationHours))
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, currentCandle.Close, "TIMEOUT"));
                            openPosition = null;
                            justClosed = true;
                        }
                    }
                    else
                    {
                        if (currentCandle.High >= openPosition.StopLoss)
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.StopLoss, "SL"));
                            openPosition = null;
                            justClosed = true;
                        }
                        else if (currentCandle.Low <= openPosition.TakeProfit)
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, openPosition.TakeProfit, "TP"));
                            openPosition = null;
                            justClosed = true;
                        }
                        else if (!disableTimeout && currentCandle.OpenTime >= openPosition.EntryTime.AddHours(effectiveEvaluationHours))
                        {
                            trades.Add(CloseTrade(openPosition, currentCandle.OpenTime, currentCandle.Close, "TIMEOUT"));
                            openPosition = null;
                            justClosed = true;
                        }
                    }
                }
            }

            if (openPosition != null || justClosed)
                continue;

            // Janela deslizante dos últimos até 300 candles do próprio ativo — mesmo lookback
            // que o scanner ao vivo usa (nunca vê mais que 300 candles de contexto real).
            int assetWindowStart = Math.Max(0, i + 1 - LookbackCandles);
            var candlesSoFar = candles.GetRange(assetWindowStart, i + 1 - assetWindowStart);

            // Avança os ponteiros do BTC até o instante atual, sem reescanear do início —
            // seguro porque candles[] e btcCandles[]/btcDailyCandles[] estão em ordem crescente de tempo.
            while (btcIndex < btcCandles.Count && btcCandles[btcIndex].OpenTime <= currentCandle.OpenTime)
                btcIndex++;
            while (btcDailyIndex < btcDailyCandles.Count && btcDailyCandles[btcDailyIndex].OpenTime <= currentCandle.OpenTime)
                btcDailyIndex++;

            // Candles diários do próprio ativo (etapa 4.2) — só existe quando o modo pediu
            // (SwingWithPartialExits); nos outros modos, symbolDailyCandles é null e essa
            // janela fica sempre vazia, sem custo extra.
            List<Candle>? symbolDailySoFar = null;
            if (symbolDailyCandles != null)
            {
                while (symbolDailyIndex < symbolDailyCandles.Count && symbolDailyCandles[symbolDailyIndex].OpenTime <= currentCandle.OpenTime)
                    symbolDailyIndex++;
                int symbolDailyWindowStart = Math.Max(0, symbolDailyIndex - LookbackCandles);
                symbolDailySoFar = symbolDailyCandles.GetRange(symbolDailyWindowStart, symbolDailyIndex - symbolDailyWindowStart);
            }

            int btcWindowStart = Math.Max(0, btcIndex - LookbackCandles);
            var btcCandlesSoFar = btcCandles.GetRange(btcWindowStart, btcIndex - btcWindowStart);

            int btcDailyWindowStart = Math.Max(0, btcDailyIndex - LookbackCandles);
            var btcDailySoFar = btcDailyCandles.GetRange(btcDailyWindowStart, btcDailyIndex - btcDailyWindowStart);

            if (btcCandlesSoFar.Count < 60 || btcDailySoFar.Count < 200)
            {
                skippedInsufficientData++;
                continue;
            }

            decimal btcEma200 = EmaIndicator.Calculate(btcDailySoFar, 200)[^1] ?? 0;
            string marketRegime = MarketRegimeIndicator.Calculate(btcDailySoFar[^1].Close, btcEma200);

            var analysis = _assetAnalyzer.Analyze(symbol, candlesSoFar, btcCandlesSoFar, profile, riskMode, symbolDailySoFar, direction);

            if (thresholds?.EnableBollingerScoring == true)
            {
                var (bbMiddle, bbUpper, bbLower, bbWidth) = BollingerBandsIndicator.Calculate(candlesSoFar);
                var scoringContext = new ScoringContext
                {
                    Candles = candlesSoFar,
                    Trend = analysis.Trend,
                    Volume = analysis.Volume,
                    Structure = analysis.Structure,
                    Risk = analysis.Risk,
                    BollingerMiddle = bbMiddle,
                    BollingerUpper = bbUpper,
                    BollingerLower = bbLower,
                    BollingerBandWidth = bbWidth,
                    AtrPercentSeries = new List<decimal?>(),
                    AdxSlope = null,
                    CandleRangePercentSeries = new List<decimal?>()
                };
                var scoringEngine = new ScoringEngine(new List<IScoringRule> { new BandWidthPercentileRule() });
                var (adjustment, _) = scoringEngine.Evaluate(scoringContext);
                analysis.OpportunityScore += adjustment;
            }

            if (thresholds?.EnableVolatilityScoringPhaseB == true)
            {
                var (bbMiddle, bbUpper, bbLower, bbWidth) = BollingerBandsIndicator.Calculate(candlesSoFar);
                var atrAbsoluteSeries = AtrSeriesCalculator.Calculate(candlesSoFar);
                var atrPercentSeries = atrAbsoluteSeries
                    .Select((v, i) => v.HasValue && candlesSoFar[i].Close != 0
                        ? (decimal?)(v.Value / candlesSoFar[i].Close * 100m)
                        : null)
                    .ToList();

                // ADX só precisa de 2 pontos (agora e N candles atrás) pra medir inclinação —
                // chama o indicador existente (que só retorna o valor mais recente) só duas
                // vezes, não recalcula uma série inteira candle a candle.
                const int adxSlopeLookback = 5;
                decimal? adxSlope = null;
                if (candlesSoFar.Count > adxSlopeLookback + 15) // margem de segurança pro período do ADX
                {
                    decimal currentAdx = AdxIndicator.Calculate(candlesSoFar);
                    var pastCandles = candlesSoFar.GetRange(0, candlesSoFar.Count - adxSlopeLookback);
                    decimal pastAdx = AdxIndicator.Calculate(pastCandles);
                    adxSlope = currentAdx - pastAdx;
                }

                var candleRangePercentSeries = candlesSoFar
                    .Select(c => c.Close != 0 ? (decimal?)((c.High - c.Low) / c.Close * 100m) : null)
                    .ToList();

                var phaseBContext = new ScoringContext
                {
                    Candles = candlesSoFar,
                    Trend = analysis.Trend,
                    Volume = analysis.Volume,
                    Structure = analysis.Structure,
                    Risk = analysis.Risk,
                    BollingerMiddle = bbMiddle,
                    BollingerUpper = bbUpper,
                    BollingerLower = bbLower,
                    BollingerBandWidth = bbWidth,
                    AtrPercentSeries = atrPercentSeries,
                    AdxSlope = adxSlope,
                    CandleRangePercentSeries = candleRangePercentSeries
                };

                var phaseBRules = new List<IScoringRule>
                {
                    new AtrLevelRule(),
                    new AtrSlopeRule(),
                    new BandExpansionRule(),
                    new CandleRangeRule(),
                    new LiquidityRule()
                };

                // O modo Swing+Buffer ATR já afasta o stop do suporte por construção própria
                // (Support = suporte real - ATR×multiplicador) — somar essa regra em cima disso
                // duplica o efeito e infla artificialmente a contagem de sinais elegíveis
                // (confirmado empiricamente: 62→139 operações no mesmo universo/período).
                if (riskMode != RiskCalculationMode.SwingWithAtrBuffer)
                    phaseBRules.Add(new StructureDistanceRule());

                var phaseBEngine = new ScoringEngine(phaseBRules);
                var (phaseBAdjustment, _) = phaseBEngine.Evaluate(phaseBContext);
                analysis.OpportunityScore += phaseBAdjustment;
            }

            var eligibility = EligibilityEvaluator.Evaluate(analysis, marketRegime, thresholds, direction);
            diagnostics.TotalAnalyzed++;

            if (eligibility.FailedScore) diagnostics.FailedScore++;
            if (eligibility.FailedBreakout) diagnostics.FailedBreakout++;
            if (eligibility.FailedConsolidation) diagnostics.FailedConsolidation++;
            if (eligibility.FailedVolumeSpike) diagnostics.FailedVolumeSpike++;
            if (eligibility.FailedResistanceDistance) diagnostics.FailedResistanceDistance++;
            if (eligibility.FailedDirection) diagnostics.FailedDirection++;
            if (eligibility.FailedRiskReward) diagnostics.FailedRiskReward++;
            if (eligibility.FailedStopDistance) diagnostics.FailedStopDistance++;
            if (eligibility.FailedRiskRewardTooHigh) diagnostics.FailedRiskRewardTooHigh++;
            if (eligibility.FailedStopDistanceTooHigh) diagnostics.FailedStopDistanceTooHigh++;
            if (eligibility.FailedBullTrap) diagnostics.FailedBullTrap++;
            if (eligibility.FailedTrendConfirmation) diagnostics.FailedTrendConfirmation++;

            if (!eligibility.IsEligible)
                continue;

            string key = analysis.Signal;
            if (lastSignalTimeByKey.TryGetValue(key, out var lastTime) &&
                currentCandle.OpenTime < lastTime.AddDays(profile.DuplicateSignalWindowDays))
            {
                diagnostics.SkippedDuplicateToday++;
                continue;
            }
            lastSignalTimeByKey[key] = currentCandle.OpenTime;

            diagnostics.PassedAll++;

            // Fase 1 do lado de venda: Long usa Resistance como alvo e Support como stop
            // (como sempre foi); Short inverte os papéis — Support (abaixo do preço) vira
            // o alvo, Resistance (acima) vira o stop.
            openPosition = new BacktestOpenPosition
            {
                Symbol = symbol,
                EntryTime = currentCandle.OpenTime,
                EntryPrice = analysis.Trend.Close,
                TakeProfit = direction == TradeDirection.Long ? analysis.Risk.Resistance : analysis.Risk.Support,
                StopLoss = direction == TradeDirection.Long ? analysis.Risk.Support : analysis.Risk.Resistance,
                Direction = direction,
                Signal = analysis.Signal,
                Score = analysis.OpportunityScore,
                ResistanceDistancePercent = analysis.Risk.ResistanceDistancePercent,
                SupportDistancePercent = analysis.Risk.SupportDistancePercent,
                RiskRewardAtEntry = analysis.Risk.RiskReward,
                TakeProfit1 = analysis.Risk.TakeProfit1,
                TakeProfit3 = analysis.Risk.TakeProfit3,
                Tp1Fraction = partialExitFractions?.Tp1 ?? 0.40m,
                Tp2Fraction = partialExitFractions?.Tp2 ?? 0.40m
            };
        }

        // Se os dados acabaram com uma posição ainda aberta (nunca bateu TP, SL nem timeout),
        // fecha ela a mercado no último candle disponível — sem isso, o trade some silenciosamente
        // do resultado, mesmo que estivesse com lucro no momento em que o teste terminou.
        if (openPosition != null)
        {
            var lastCandle = candles[^1];
            if (openPosition.TakeProfit1.HasValue)
            {
                decimal legReturn = (lastCandle.Close - openPosition.EntryPrice) / openPosition.EntryPrice * 100m;
                openPosition.WeightedExitSum += openPosition.RemainingFraction * legReturn;
                string reason = openPosition.Tp1Hit
                    ? (openPosition.Tp2Hit ? "TP1TP2EOT" : "TP1EOT")
                    : "EOT";
                trades.Add(CloseTradeWeighted(openPosition, lastCandle.OpenTime, reason));
            }
            else
            {
                trades.Add(CloseTrade(openPosition, lastCandle.OpenTime, lastCandle.Close, "EOT"));
            }
        }

        if (skippedInsufficientData > 0 && diagnostics.TotalAnalyzed == 0)
            System.Diagnostics.Debug.WriteLine($"[Backtest] {symbol}: todas as {skippedInsufficientData} janelas puladas por falta de candles de BTC suficientes.");

        return (trades, diagnostics);
    }

    public static void MergeDiagnostics(FilterDiagnostics target, FilterDiagnostics source)
    {
        target.TotalAnalyzed += source.TotalAnalyzed;
        target.PassedAll += source.PassedAll;
        target.FailedScore += source.FailedScore;
        target.FailedBreakout += source.FailedBreakout;
        target.FailedConsolidation += source.FailedConsolidation;
        target.FailedVolumeSpike += source.FailedVolumeSpike;
        target.FailedResistanceDistance += source.FailedResistanceDistance;
        target.FailedDirection += source.FailedDirection;
        target.FailedRiskReward += source.FailedRiskReward;
        target.SkippedDuplicateToday += source.SkippedDuplicateToday;
        target.FailedStopDistance += source.FailedStopDistance;
        target.FailedRiskRewardTooHigh += source.FailedRiskRewardTooHigh;
        target.FailedStopDistanceTooHigh += source.FailedStopDistanceTooHigh;
        target.FailedBullTrap += source.FailedBullTrap;
        target.FailedTrendConfirmation += source.FailedTrendConfirmation;
    }

    private static BacktestTradeResult CloseTrade(BacktestOpenPosition position, DateTime exitTime, decimal exitPrice, string reason)
    {
        // Fase 1 do lado de venda: Long lucra com o preço subindo, Short lucra com o preço
        // descendo — o sinal do cálculo se inverte conforme a direção da posição.
        decimal outcomePercent = position.Direction == TradeDirection.Long
            ? ((exitPrice - position.EntryPrice) / position.EntryPrice) * 100m
            : ((position.EntryPrice - exitPrice) / position.EntryPrice) * 100m;

        return new BacktestTradeResult
        {
            Symbol = position.Symbol,
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            ExitTime = exitTime,
            ExitPrice = exitPrice,
            OutcomePercent = outcomePercent,
            ExitReason = reason,
            Signal = position.Signal,
            Score = position.Score,
            Direction = position.Direction,
            ResistanceDistancePercent = position.ResistanceDistancePercent,
            SupportDistancePercent = position.SupportDistancePercent,
            RiskRewardAtEntry = position.RiskRewardAtEntry
        };
    }

    /// <summary>
    /// Fecha uma posição com saída parcial (etapa 4.3) — o resultado final é a média
    /// ponderada de todas as pernas já realizadas (TP1/TP2/TP3/SL/timeout), somadas em
    /// WeightedExitSum ao longo do tempo. O ExitPrice é reconstruído a partir desse
    /// percentual combinado, pra continuar cabendo na estrutura de trade de sempre
    /// (1 linha por operação, como decidido) sem precisar mudar BacktestTradeResult.
    /// Long apenas — TakeProfit1 nunca é preenchido pra Short (ver AssetAnalyzer), então
    /// esse caminho nunca é exercitado por uma posição vendida.
    /// </summary>
    private static BacktestTradeResult CloseTradeWeighted(BacktestOpenPosition position, DateTime exitTime, string reason)
    {
        decimal outcomePercent = position.WeightedExitSum;
        decimal exitPrice = position.EntryPrice * (1 + outcomePercent / 100m);
        return new BacktestTradeResult
        {
            Symbol = position.Symbol,
            EntryTime = position.EntryTime,
            EntryPrice = position.EntryPrice,
            ExitTime = exitTime,
            ExitPrice = exitPrice,
            OutcomePercent = outcomePercent,
            ExitReason = reason,
            Signal = position.Signal,
            Score = position.Score,
            Direction = position.Direction,
            ResistanceDistancePercent = position.ResistanceDistancePercent,
            SupportDistancePercent = position.SupportDistancePercent,
            RiskRewardAtEntry = position.RiskRewardAtEntry
        };
    }

    /// <summary>
    /// Processa saída parcial (TP1 → breakeven → TP2 → TP3) pra um candle. Só entra em
    /// ação quando a posição tem TakeProfit1 definido (modo SwingWithPartialExits) — nos
    /// outros modos, o chamador usa o fechamento único original, sem chamar isso aqui.
    /// Retorna true se a posição foi fechada por completo nesse candle.
    /// Limitação conhecida: assume no máximo 1 "passo" de progressão por nível por candle
    /// (mesma simplificação que o resto do motor já usa pra SL-vs-TP no mesmo candle —
    /// dado real intra-candle não é conhecível a partir de OHLC).
    /// </summary>
    private static bool ProcessPartialExits(BacktestOpenPosition position, Candle currentCandle, List<BacktestTradeResult> trades)
    {
        // 1. Stop Loss sempre tem prioridade — pode já estar no breakeven se TP1 bateu antes.
        if (currentCandle.Low <= position.StopLoss)
        {
            decimal legReturn = (position.StopLoss - position.EntryPrice) / position.EntryPrice * 100m;
            position.WeightedExitSum += position.RemainingFraction * legReturn;
            string reason = position.Tp1Hit
                ? (position.Tp2Hit ? "TP1TP2SL" : "TP1SL")
                : "SL";
            trades.Add(CloseTradeWeighted(position, currentCandle.OpenTime, reason));
            return true;
        }

        // 2. TP1 — realiza a fração configurada (padrão 40%), move o stop pro breakeven.
        if (!position.Tp1Hit && position.TakeProfit1.HasValue && currentCandle.High >= position.TakeProfit1.Value)
        {
            decimal tp1Fraction = position.Tp1Fraction;
            decimal legReturn = (position.TakeProfit1.Value - position.EntryPrice) / position.EntryPrice * 100m;

            position.WeightedExitSum += tp1Fraction * legReturn;
            position.RemainingFraction -= tp1Fraction;
            position.Tp1Hit = true;
            position.StopLoss = position.EntryPrice * 1.001m; // breakeven + 0,1% de folga
        }

        // 3. TP2 — a resistência estrutural (o TakeProfit "principal" de sempre). Realiza
        // a fração configurada (padrão 40%). Só pode disparar depois do TP1 (matematicamente
        // TP1 sempre fica mais perto, já que TP1 = Entrada + 60% × (TP2 - Entrada)).
        if (position.Tp1Hit && !position.Tp2Hit && currentCandle.High >= position.TakeProfit)
        {
            decimal tp2Fraction = position.Tp2Fraction;
            decimal legReturn = (position.TakeProfit - position.EntryPrice) / position.EntryPrice * 100m;

            position.WeightedExitSum += tp2Fraction * legReturn;
            position.RemainingFraction -= tp2Fraction;
            position.Tp2Hit = true;
        }

        // 4. TP3 — fecha o restante da posição (o que sobrar depois de TP1+TP2).
        if (position.Tp2Hit && position.TakeProfit3.HasValue && currentCandle.High >= position.TakeProfit3.Value)
        {
            decimal legReturn = (position.TakeProfit3.Value - position.EntryPrice) / position.EntryPrice * 100m;
            position.WeightedExitSum += position.RemainingFraction * legReturn;
            trades.Add(CloseTradeWeighted(position, currentCandle.OpenTime, "TP1TP2TP3"));
            return true;
        }

        return false;
    }

    public static BacktestSummary BuildSummary(List<BacktestTradeResult> trades, FilterDiagnostics diagnostics, List<string> skippedSymbols)
    {
        var ordered = trades.OrderBy(t => t.ExitTime).ToList();
        int total = ordered.Count;
        int wins = ordered.Count(t => t.OutcomePercent > 0);
        double winRate = total > 0 ? wins * 100.0 / total : 0;
        decimal totalReturn = ordered.Sum(t => t.OutcomePercent);

        decimal grossProfit = ordered.Where(t => t.OutcomePercent > 0).Sum(t => t.OutcomePercent);
        decimal grossLoss = Math.Abs(ordered.Where(t => t.OutcomePercent < 0).Sum(t => t.OutcomePercent));
        // 999999 em vez de decimal.MaxValue: o SQLite guarda REAL como double (menos preciso
        // que decimal), e decimal.MaxValue não sobrevive intacto a essa ida-e-volta — causa
        // OverflowException ao ler de volta. 999999 significa a mesma coisa na prática
        // ("basicamente infinito", sem perda alguma) e é seguro pra qualquer tipo de coluna.
        decimal profitFactor = grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? 999999m : 0);

        decimal runningTotal = 0;
        decimal peak = 0;
        decimal maxDrawdown = 0;
        foreach (var trade in ordered)
        {
            runningTotal += trade.OutcomePercent;
            if (runningTotal > peak)
                peak = runningTotal;
            decimal drawdown = peak - runningTotal;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        // Win Rate de equilíbrio: dado o RR médio real de entrada, qual seria o
        // percentual mínimo de acerto pra não ganhar nem perder dinheiro.
        decimal avgRiskReward = total > 0 ? ordered.Average(t => t.RiskRewardAtEntry) : 0;
        double breakEvenWinRate = avgRiskReward > 0 ? 100.0 / (1 + (double)avgRiskReward) : 0;
        double edge = winRate - breakEvenWinRate;

        return new BacktestSummary
        {
            TotalTrades = total,
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            MaxDrawdownPercent = maxDrawdown,
            ProfitFactor = profitFactor,
            Trades = ordered,
            Diagnostics = diagnostics,
            SkippedSymbols = skippedSymbols,
            AvgRiskRewardAtEntry = avgRiskReward,
            BreakEvenWinRate = breakEvenWinRate,
            Edge = edge
        };
    }

    private sealed class BacktestOpenPosition
    {
        public required string Symbol { get; init; }
        public required DateTime EntryTime { get; init; }
        public required decimal EntryPrice { get; init; }
        public required decimal TakeProfit { get; init; }
        public decimal StopLoss { get; set; } // mutável — move pro breakeven após TP1 (etapa 4.3)
        public required TradeDirection Direction { get; init; } // Fase 1 do lado de venda
        public required string Signal { get; init; }
        public required decimal Score { get; init; }
        public required decimal ResistanceDistancePercent { get; init; }
        public required decimal SupportDistancePercent { get; init; }
        public required decimal RiskRewardAtEntry { get; init; }
        // Campos da etapa 4.3 (saída parcial) — só usados quando TakeProfit1 tem valor
        // (ou seja, só no modo SwingWithPartialExits, e só Long). Nos outros casos, ficam
        // null/0 e o laço principal usa o comportamento original de fechamento único.
        public decimal? TakeProfit1 { get; init; }
        public decimal? TakeProfit3 { get; init; }
        public decimal Tp1Fraction { get; init; } = 0.40m; // configurável — etapa 4.3a
        public decimal Tp2Fraction { get; init; } = 0.40m;
        public decimal RemainingFraction { get; set; } = 1.0m;
        public bool Tp1Hit { get; set; }
        public bool Tp2Hit { get; set; }
        public decimal WeightedExitSum { get; set; } // soma ponderada (fração × retorno%) das pernas já realizadas
    }
}