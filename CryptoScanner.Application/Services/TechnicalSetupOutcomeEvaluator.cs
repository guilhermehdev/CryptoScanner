using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Avalia setups observacionais depois de 24 horas. Não usa stop, alvo ou custos,
/// pois mede a qualidade do padrão antes de qualquer regra de execução.
/// </summary>
public sealed class TechnicalSetupOutcomeEvaluator(ISignalRepository repository, IMarketDataService marketData)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> EvaluateDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return 0;

        try
        {
            var due = await repository.GetTechnicalSetupAlertsDueForEvaluationAsync(DateTime.UtcNow.AddHours(-24), 25, cancellationToken);
            int completed = 0;
            foreach (var alert in due)
            {
                try
                {
                    var interval = TimeSpan.FromHours(1);
                    var candles = await marketData.GetHistoricalCandlesAsync(
                        alert.Symbol, "1h", alert.EntryUtc, alert.EntryUtc.AddHours(25), cancellationToken);
                    var after1Hour = FindFirstClosedAtOrAfter(candles, alert.EntryUtc.AddHours(1), interval);
                    var after6Hours = FindFirstClosedAtOrAfter(candles, alert.EntryUtc.AddHours(6), interval);
                    var after24Hours = FindFirstClosedAtOrAfter(candles, alert.EntryUtc.AddHours(24), interval);
                    if (after1Hour < 0 || after6Hours < 0 || after24Hours < 0 || alert.Price <= 0)
                        continue;

                    decimal DirectionalReturn(decimal price) => alert.Direction == TradeDirection.Long
                        ? (price - alert.Price) / alert.Price * 100m
                        : (alert.Price - price) / alert.Price * 100m;

                    decimal favorable = 0;
                    decimal adverse = 0;
                    for (int index = 0; index <= after24Hours; index++)
                    {
                        var candle = candles[index];
                        favorable = Math.Max(favorable, alert.Direction == TradeDirection.Long
                            ? DirectionalReturn(candle.High)
                            : DirectionalReturn(candle.Low));
                        adverse = Math.Max(adverse, alert.Direction == TradeDirection.Long
                            ? -DirectionalReturn(candle.Low)
                            : -DirectionalReturn(candle.High));
                    }

                    await repository.UpdateTechnicalSetupAlertOutcomeAsync(
                        alert.Id,
                        DirectionalReturn(candles[after1Hour].Close),
                        DirectionalReturn(candles[after6Hours].Close),
                        DirectionalReturn(candles[after24Hours].Close),
                        favorable,
                        adverse,
                        cancellationToken);
                    completed++;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // O registro continua pendente e será tentado novamente numa varredura futura.
                }
            }

            return completed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int FindFirstClosedAtOrAfter(IReadOnlyList<Candle> candles, DateTime targetUtc, TimeSpan interval)
    {
        for (int index = 0; index < candles.Count; index++)
            if (candles[index].OpenTime + interval >= targetUtc)
                return index;
        return -1;
    }
}
