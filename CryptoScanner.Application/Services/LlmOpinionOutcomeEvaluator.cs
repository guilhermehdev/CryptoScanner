using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Utilities;

namespace CryptoScanner.Application.Services;

// Paper evaluation of validated LLM recommendations. It never opens a real or simulated trade.
public sealed class LlmOpinionOutcomeEvaluator
{
    private readonly ILlmOpinionRepository _opinions;
    private readonly IMarketDataService _marketData;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LlmOpinionOutcomeEvaluator(ILlmOpinionRepository opinions, IMarketDataService marketData)
    {
        _opinions = opinions;
        _marketData = marketData;
    }

    public async Task<int> EvaluateDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return 0;

        try
        {
            await _opinions.InitializeAsync(cancellationToken);
            var pending = await _opinions.GetPendingRecommendationsAsync(cancellationToken);
            int evaluated = 0;

            foreach (var opinion in pending)
            {
                if (opinion.Entry is not decimal entry || opinion.Stop is not decimal stop || opinion.Tp2 is not decimal target)
                    continue;

                var profile = GetProfile(opinion.Profile);
                try
                {
                    DateTime startedAt = opinion.CreatedAt.ToUniversalTime();
                    DateTime deadline = startedAt.AddHours(profile.EvaluationHours);
                    DateTime now = DateTime.UtcNow;
                    bool isDue = now >= deadline;
                    TimeSpan interval = CandleIntervalHelper.ToTimeSpan(profile.CandleInterval);
                    DateTime historyEnd = isDue ? deadline.Add(interval) : now;
                    var candles = await _marketData.GetHistoricalCandlesAsync(
                        opinion.Symbol, profile.CandleInterval, startedAt, historyEnd, cancellationToken);

                    var result = Evaluate(opinion, candles, entry, stop, target, deadline);
                    if (result is null && isDue)
                        result = EvaluateTimeout(opinion, candles, entry, deadline, interval);
                    if (result is null)
                        continue;

                    await _opinions.UpdateOpinionOutcomeAsync(
                        opinion.Id,
                        result.Value.OutcomePercent,
                        result.Value.Reason,
                        result.Value.OutcomeAt,
                        cancellationToken);
                    evaluated++;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // A cotação pode falhar ou o ativo pode ter sido removido; tenta de novo no próximo ciclo.
                }
            }

            return evaluated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static EvaluationResult? Evaluate(
        LlmOpinionRecord opinion,
        IReadOnlyList<Candle> candles,
        decimal entry,
        decimal stop,
        decimal target,
        DateTime deadline)
    {
        bool isShort = string.Equals(opinion.Direction, "SHORT", StringComparison.OrdinalIgnoreCase);
        DateTime startedAt = opinion.CreatedAt.ToUniversalTime();

        foreach (var candle in candles
                     .Where(candle => candle.OpenTime >= startedAt && candle.OpenTime < deadline)
                     .OrderBy(candle => candle.OpenTime))
        {
            // If TP and SL occur inside the same candle, choose SL: a conservative, reproducible rule.
            bool stopHit = isShort ? candle.High >= stop : candle.Low <= stop;
            bool targetHit = isShort ? candle.Low <= target : candle.High >= target;
            if (!stopHit && !targetHit)
                continue;

            decimal exit = stopHit ? stop : target;
            decimal outcome = isShort ? (entry - exit) / entry * 100m : (exit - entry) / entry * 100m;
            return new EvaluationResult(Math.Round(outcome, 2), stopHit ? "SL" : "TP2", candle.OpenTime);
        }

        return null;
    }

    private static EvaluationResult? EvaluateTimeout(
        LlmOpinionRecord opinion,
        IReadOnlyList<Candle> candles,
        decimal entry,
        DateTime deadline,
        TimeSpan interval)
    {
        // Usa o primeiro candle fechado no prazo (ou logo após ele), nunca a cotação atual.
        // Assim uma avaliação atrasada continua representando o período original da opinião.
        var timeoutCandle = candles
            .Where(candle => candle.OpenTime >= opinion.CreatedAt.ToUniversalTime() && candle.OpenTime + interval >= deadline)
            .OrderBy(candle => candle.OpenTime)
            .FirstOrDefault();
        if (timeoutCandle is null)
            return null;

        bool isShort = string.Equals(opinion.Direction, "SHORT", StringComparison.OrdinalIgnoreCase);
        decimal outcome = isShort
            ? (entry - timeoutCandle.Close) / entry * 100m
            : (timeoutCandle.Close - entry) / entry * 100m;
        return new EvaluationResult(Math.Round(outcome, 2), "TIMEOUT", timeoutCandle.OpenTime + interval);
    }

    private static ScanProfile GetProfile(string profile) => profile switch
    {
        var name when name == ScanProfile.Scalp.Name => ScanProfile.Scalp,
        var name when name == ScanProfile.Intraday.Name => ScanProfile.Intraday,
        _ => ScanProfile.Swing
    };

    private readonly record struct EvaluationResult(decimal OutcomePercent, string Reason, DateTime OutcomeAt);
}
