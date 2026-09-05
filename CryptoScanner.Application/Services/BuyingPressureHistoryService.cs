using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public sealed class BuyingPressureHistoryService(IBuyingPressureRepository repository, IBuyingPressurePriceSource priceSource)
{
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);

    public async Task RecordAsync(string symbol, MarketFlowData flow, BuyingPressureResult result,
        DateTimeOffset collectedAt, CancellationToken cancellationToken = default)
    {
        long collectedMs = collectedAt.ToUnixTimeMilliseconds();
        long end = result.Measurements?.WindowEndMs ?? collectedMs / 300_000 * 300_000;
        await repository.SaveAsync(new(symbol.ToUpperInvariant(), end, collectedMs, result, flow), cancellationToken);
        // Reuse prices already fetched by the scanner before requesting historical endpoints.
        var prices = flow.PressureCandles.Where(c => c.Close > 0 && c.Low > 0 &&
                c.High >= Math.Max(c.Open, c.Close) && c.Low <= Math.Min(c.Open, c.Close) &&
                c.OpenTime + 300_000 <= collectedMs)
            .Select(c => new PressurePrice(symbol.ToUpperInvariant(), c.OpenTime + 300_000,
                c.Close, collectedMs, collectedMs - (c.OpenTime + 300_000) > 300_000)).ToArray();
        await repository.SavePricesAsync(prices, cancellationToken);
    }

    public async Task CompleteDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!await _evaluationGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            // Bounded catch-up after downtime; work remains durable in SQLite between runs.
            var due = await repository.GetDueTargetsAsync(now.ToUnixTimeMilliseconds(), 20, cancellationToken);
            foreach (var target in due)
            {
                decimal? price = await priceSource.GetFuturesCloseAsync(target.Symbol, target.CloseTimeMs, cancellationToken);
                if (price is > 0)
                    await repository.SavePricesAsync([new(target.Symbol, target.CloseTimeMs, price.Value,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true)], cancellationToken);
            }
        }
        finally { _evaluationGate.Release(); }
    }
}
