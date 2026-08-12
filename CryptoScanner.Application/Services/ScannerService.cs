using CryptoScanner.Application.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Application.Services;

public sealed class ScannerService
{
    private const int MaxConcurrentAnalyses = 8;

    // Configuração validada via Backtest — cada perfil tem sua própria calibração, já que
    // Swing (candles de 4h) e Intraday (candles de 1h) responderam de forma bem diferente
    // aos mesmos parâmetros nos testes (RR=1,5 e Dist.=4% funcionam bem no Swing, mas geram
    // resultado ruim no Intraday sem esse ajuste separado).
    // Ainda não inclui saída parcial de verdade no Diário (isso é a etapa 3.3/3.4);
    // por enquanto só troca QUAL sinal é considerado elegível.
    private const RiskCalculationMode ValidatedRiskMode = RiskCalculationMode.SwingWithPartialExits;

    private static readonly EligibilityThresholds SwingValidatedThresholds = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // não usado nesse modo
        MinResistanceDistancePartialExits = 4m, // validado no Backtest (Swing)
        MinRiskReward = 2.0m, // atualizado de 1,5 pra 2,0 — recalibração necessária pra ativar o Caminho A
                              // com segurança (ver comentário em EnablePullbackBounce abaixo)
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m, // testado e confirmado irrelevante nesse modo (buffer de ATR já protege)
        MaxStopDistancePercent = 25m, // validado via Backtest em 3 universos distintos (110/167 moedas,
                                      // 3 janelas de período) — faixa 20-30% consistentemente melhor que
                                      // os extremos; 25% escolhido como meio-termo robusto entre eles,
                                      // evitando depender do pico exato de um teste isolado
        MaxRiskReward = 999m, // mesmo valor usado em toda a validação
        EnablePullbackBounce = true, // validado via Backtest em 2 janelas de período distintas (2020-2026 e
                                     // 2021-2026, 171 moedas) — só funciona bem COMBINADO com RR mín.=2,0;
                                     // com RR=1,5 (valor antigo), o Caminho A piorava tudo (PF 1,52, DD 43,16%
                                     // vs baseline 2,79/31,81%). Com RR=2,0: mais operações que o baseline
                                     // (21-25 vs 19), Profit Factor maior (3,00-3,70 vs 2,79), Drawdown menor
        EnableBollingerScoring = true, // validado via Backtest em 2 janelas de período distintas (2020-2026
                                       // e 2021-2026, 167 moedas, RR mín.=1,5) — melhora Win Rate, Profit
                                       // Factor, Retorno E reduz Drawdown ao mesmo tempo, nas duas vezes;
                                       // diferente da Fase B, não dilui o sinal da resistência pontuada
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    private static readonly EligibilityThresholds IntradayValidatedThresholds = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // não usado nesse modo
        MinResistanceDistancePartialExits = 15m, // validado no Backtest (Intraday) — RR=1,5/Dist.=4% do
                                                 // Swing geram resultado ruim aqui (PF 0,58, DD 131,6%);
                                                 // Dist.≥15% confirmado em 2 universos distintos (167
                                                 // moedas/2020-2026 e 171 moedas/2020-2024), PF 4,45-5,64
        MinRiskReward = 2.5m, // validado no Backtest (Intraday) — comparador de RR mín. mostrou pico
                              // claro em 2,5 (PF 2,90 com 19 trades), caindo dos dois lados
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m, // mesmo valor do Swing — não testado separadamente no Intraday ainda
        MaxStopDistancePercent = 25m, // mesmo valor do Swing — não testado separadamente no Intraday ainda,
                                      // reutilizado por analogia
        MaxRiskReward = 999m,
        EnablePullbackBounce = false,
        EnableBollingerScoring = true, // já estava ativo em todos os testes de calibração do Intraday
                                       // (RR mín. e Dist. Resist.), então tecnicamente confirmado nesse
                                       // contexto também, ainda que não isolado especificamente
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    // ATENÇÃO: NÃO VALIDADO — valores de partida conservadores (mais rígidos que o Intraday,
    // seguindo o padrão observado de "timeframe menor exige filtro mais apertado"), só pra
    // destravar testes no Backtest. Não expor esse perfil no app ao vivo (MainWindow) até
    // rodar o ciclo completo de calibração (RR, Dist. Resist., Stop, Bollinger) e confirmar
    // resultado em pelo menos 2 universos/períodos distintos — mesma disciplina usada pro
    // Swing e pro Intraday.
    private static readonly EligibilityThresholds ScalpValidatedThresholds = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // não usado nesse modo
        MinResistanceDistancePartialExits = 20m, // NÃO VALIDADO — chute inicial, mais rígido que os 15% do Intraday
        MinRiskReward = 3.0m, // NÃO VALIDADO — chute inicial, mais rígido que os 2,5 do Intraday
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxStopDistancePercent = 25m, // reutilizado por analogia — não testado nesse perfil
        MaxRiskReward = 999m,
        EnablePullbackBounce = false,
        EnableBollingerScoring = true, // reutilizado por analogia — não testado nesse perfil
        EnableVolatilityScoringPhaseB = false,
        EnableMultiTimeframe = false,
    };

    private static EligibilityThresholds GetValidatedThresholds(ScanProfile profile) =>
        profile.Name == ScanProfile.Intraday.Name ? IntradayValidatedThresholds :
        profile.Name == ScanProfile.Scalp.Name ? ScalpValidatedThresholds :
        SwingValidatedThresholds;

    private readonly IMarketDataService _marketData;
    private readonly ISignalRepository _signals;
    private readonly IWatchlistRepository _watchlist;
    private readonly AssetAnalyzer _assetAnalyzer;

    public ScannerService(
        IMarketDataService marketData,
        ISignalRepository signals,
        IWatchlistRepository watchlist,
        AssetAnalyzer assetAnalyzer)
    {
        _marketData = marketData;
        _signals = signals;
        _watchlist = watchlist;
        _assetAnalyzer = assetAnalyzer;
    }

    public async Task<ScannerRunResult> RunAsync(ScanProfile profile, CancellationToken cancellationToken = default)
    {
        await _signals.InitializeAsync(cancellationToken);
        await _watchlist.InitializeAsync(cancellationToken);

        var btcDailyCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1d", 300, cancellationToken);
        decimal btcEma200 = EmaIndicator.Calculate(btcDailyCandles, 200)[^1] ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcDailyCandles[^1].Close, btcEma200);

        var btcCandles = await _marketData.GetCandlesAsync("BTCUSDT", profile.CandleInterval, 300, cancellationToken);

        var favoriteSymbols = await _watchlist.GetAllAsync(cancellationToken);
        var favoriteSet = new HashSet<string>(favoriteSymbols, StringComparer.OrdinalIgnoreCase);

        var pendingSignals = await _signals.GetPendingSignalsAsync(cancellationToken);

        var topSymbols = (await _marketData.GetUsdtSymbolsAsync(cancellationToken))
            .Take(ScannerSettings.MaxCoins)
            .ToList();

        // Favoritos entram na análise mesmo que não estejam entre as mais líquidas do momento.
        var symbols = topSymbols
            .Union(favoriteSymbols, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var throttle = new SemaphoreSlim(MaxConcurrentAnalyses);
        var analyses = symbols.Select(symbol => AnalyzeSymbolAsync(symbol, btcCandles, profile, throttle, cancellationToken));
        var allAnalyzed = (await Task.WhenAll(analyses)).OfType<AssetAnalysis>().ToList();

        var top30 = allAnalyzed.OrderByDescending(a => a.OpportunityScore).Take(30).ToList();
        var missingFavorites = allAnalyzed
            .Where(a => favoriteSet.Contains(a.Symbol) && !top30.Any(t => t.Symbol == a.Symbol));

        // Favoritos aparecem sempre no ranking, mesmo fora do Top 30 por score.
        var analysesResult = top30.Concat(missingFavorites)
            .OrderByDescending(a => a.OpportunityScore)
            .ToList();

        await EvaluatePendingSignalsAsync(pendingSignals, profile, cancellationToken);

        var (diagnostics, newSignals) = await PersistEligibleSignalsAsync(analysesResult, marketRegime, profile, cancellationToken);

        var history = await _signals.GetSignalsAsync(cancellationToken);

        return new ScannerRunResult
        {
            MarketRegime = marketRegime,
            Ranking = analysesResult.Select(asset => AssetScoreFactory.Create(asset, marketRegime, favoriteSet, GetValidatedThresholds(profile))).ToList(),
            History = history,
            WinRate = await _signals.GetWinRateAsync(cancellationToken),
            AverageReturn = await _signals.GetAverageReturnAsync(cancellationToken),
            Diagnostics = diagnostics,
            NewSignals = newSignals
        };
    }

    private async Task<(FilterDiagnostics Diagnostics, List<NewSignalAlert> NewSignals)> PersistEligibleSignalsAsync(
    IReadOnlyList<AssetAnalysis> ranking,
    string marketRegime,
    ScanProfile profile,
    CancellationToken cancellationToken)
    {
        var diagnostics = new FilterDiagnostics { TotalAnalyzed = ranking.Count };
        var newSignals = new List<NewSignalAlert>();
        var thresholds = GetValidatedThresholds(profile);

        foreach (var asset in ranking)
        {
            var eligibility = EligibilityEvaluator.Evaluate(asset, marketRegime, thresholds);

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

            if (!eligibility.IsEligible)
                continue;

            if (await _signals.SignalExistsWithinWindowAsync(asset.Symbol, asset.Signal, profile.DuplicateSignalWindowDays, cancellationToken))
            {
                diagnostics.SkippedDuplicateToday++;
                continue;
            }

            diagnostics.PassedAll++;

            var snapshot = new SignalSnapshot
            {
                Symbol = asset.Symbol,
                Price = asset.Trend.Close,
                Score = asset.OpportunityScore,
                Signal = asset.Signal,
                PreviousScore = asset.PreviousScore,
                TakeProfit = asset.Risk.Resistance,
                StopLoss = asset.Risk.Support,
                Profile = profile.Name,
                MarketRegime = marketRegime,
                Rsi = asset.Trend.Rsi,
                Adx = asset.Trend.Adx,
                AtrPercent = asset.Trend.AtrPercent,
                EmaDistanceAtr = asset.Setup.EmaDistanceAtr,
                SwingUsageAtr = asset.Setup.SwingUsageAtr,
                VolumeSpike = asset.Volume.Spike,
                VolumeImbalance = asset.Volume.Imbalance,
                RelativeStrength = asset.Setup.RelativeStrength,
                RiskReward = asset.Risk.RiskReward,
                TrendScore = asset.Trend.Score,
                StructureScore = asset.Structure.Score,
                VolumeScore = asset.Volume.Score,
                CandleScore = asset.Candle.Score,
                SetupScore = asset.Setup.Score,
                MomentumScore = asset.Trend.MomentumScore,
                VolatilityScore = asset.Trend.VolatilityScore,
                TrendStrengthScore = asset.Trend.TrendStrengthScore,
                PatternName = asset.Candle.PatternName,
                SmartMoneyLabel = asset.Structure.SmartMoneyLabel,
                BreakoutSource = asset.Setup.IsBreakout ? "Clássico" :
                                  asset.Setup.IsShortTermBreakout ? "Curto Prazo" :
                                  asset.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent ? "Força Rel." : "",
                IsBullTrap = asset.Structure.IsBullTrap,
                IsBearTrap = asset.Structure.IsBearTrap
            };

            await _signals.InsertSignalAsync(snapshot, cancellationToken);
            newSignals.Add(new NewSignalAlert(asset.Symbol, asset.Signal, asset.OpportunityScore, asset.Trend.Close, profile.Name));
        }

        return (diagnostics, newSignals);
    }

    private async Task<AssetAnalysis?> AnalyzeSymbolAsync(string symbol, List<Candle> btcCandles, ScanProfile profile, SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            var candles = await _marketData.GetCandlesAsync(symbol, profile.CandleInterval, 300, cancellationToken);
            return _assetAnalyzer.Analyze(symbol, candles, btcCandles, profile, ValidatedRiskMode);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task EvaluatePendingSignalsAsync(IReadOnlyList<SignalHistory> pendingSignals, ScanProfile profile, CancellationToken cancellationToken)
    {
        foreach (var signal in pendingSignals)
        {
            int evaluationHours = signal.Profile == ScanProfile.Intraday.Name
                ? ScanProfile.Intraday.EvaluationHours
                : signal.Profile == ScanProfile.Swing.Name
                    ? ScanProfile.Swing.EvaluationHours
                    : signal.Profile == ScanProfile.Scalp.Name
                        ? ScanProfile.Scalp.EvaluationHours
                        : profile.EvaluationHours;

            if (signal.TakeProfit <= 0 || signal.StopLoss <= 0)
            {
                if (DateTime.UtcNow < signal.Timestamp.AddHours(evaluationHours))
                    continue;

                decimal legacyPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
                decimal legacyOutcome = ((legacyPrice - signal.Price) / signal.Price) * 100m;
                await _signals.UpdateSignalResultAsync(signal.Id, legacyPrice, legacyOutcome, "TIMEOUT", cancellationToken);
                continue;
            }

            List<Candle> candles;
            try
            {
                candles = await _marketData.GetCandlesAsync(signal.Symbol, "1h", evaluationHours + 24, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            var relevant = candles.Where(c => c.OpenTime >= signal.Timestamp).OrderBy(c => c.OpenTime).ToList();

            bool hitTakeProfit = false;
            bool hitStopLoss = false;
            decimal exitPrice = signal.Price;

            foreach (var candle in relevant)
            {
                if (candle.Low <= signal.StopLoss)
                {
                    hitStopLoss = true;
                    exitPrice = signal.StopLoss;
                    break;
                }
                if (candle.High >= signal.TakeProfit)
                {
                    hitTakeProfit = true;
                    exitPrice = signal.TakeProfit;
                    break;
                }
            }

            bool timeoutReached = DateTime.UtcNow >= signal.Timestamp.AddHours(evaluationHours);

            if (hitTakeProfit || hitStopLoss)
            {
                decimal outcomePercent = ((exitPrice - signal.Price) / signal.Price) * 100m;
                string reason = hitTakeProfit ? "TP" : "SL";
                await _signals.UpdateSignalResultAsync(signal.Id, exitPrice, outcomePercent, reason, cancellationToken);
            }
            else if (timeoutReached)
            {
                decimal currentPrice = await _marketData.GetCurrentPriceAsync(signal.Symbol, cancellationToken);
                decimal outcomePercent = ((currentPrice - signal.Price) / signal.Price) * 100m;
                await _signals.UpdateSignalResultAsync(signal.Id, currentPrice, outcomePercent, "TIMEOUT", cancellationToken);
            }
        }
    }

    public async Task<AssetScore?> LookupSymbolAsync(string symbol, ScanProfile profile, CancellationToken cancellationToken = default)
    {
        await _watchlist.InitializeAsync(cancellationToken);

        List<Candle> candles;
        try
        {
            candles = await _marketData.GetCandlesAsync(symbol, profile.CandleInterval, 300, cancellationToken);
        }
        catch
        {
            return null;
        }

        if (candles.Count < 200)
            return null;

        var btcDailyCandles = await _marketData.GetCandlesAsync("BTCUSDT", "1d", 300, cancellationToken);
        decimal btcEma200 = EmaIndicator.Calculate(btcDailyCandles, 200)[^1] ?? 0;
        string marketRegime = MarketRegimeIndicator.Calculate(btcDailyCandles[^1].Close, btcEma200);

        var btcCandles = await _marketData.GetCandlesAsync("BTCUSDT", profile.CandleInterval, 300, cancellationToken);

        var favoriteSymbols = await _watchlist.GetAllAsync(cancellationToken);
        var favoriteSet = new HashSet<string>(favoriteSymbols, StringComparer.OrdinalIgnoreCase);

        var analysis = _assetAnalyzer.Analyze(symbol, candles, btcCandles, profile, ValidatedRiskMode);
        return AssetScoreFactory.Create(analysis, marketRegime, favoriteSet, GetValidatedThresholds(profile));
    }
}