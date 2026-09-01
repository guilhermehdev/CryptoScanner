using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public sealed class CachingMarketDataService : IMarketDataService
{
    private readonly IMarketDataService _inner;
    private readonly ICandleCacheRepository _cache;

    public CachingMarketDataService(IMarketDataService inner, ICandleCacheRepository cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<MarketFlowData> GetMarketFlowDataAsync(string symbol, CancellationToken cancellationToken = default)
    => _inner.GetMarketFlowDataAsync(symbol, cancellationToken);

    // Dados em tempo real (usados pelo scanner ao vivo) passam direto, sem cache —
    // fazem sentido só se estiverem sempre atualizados.
    public Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 1000, CancellationToken cancellationToken = default)
        => _inner.GetCandlesAsync(symbol, interval, limit, cancellationToken);

    public Task<List<string>> GetUsdtSymbolsAsync(CancellationToken cancellationToken = default)
        => _inner.GetUsdtSymbolsAsync(cancellationToken);

    public Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken cancellationToken = default)
        => _inner.GetCurrentPriceAsync(symbol, cancellationToken);

    // Histórico (usado só pelo backtest) passa pelo cache local primeiro.
    public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        await _cache.InitializeAsync(cancellationToken);

        bool covered = await _cache.IsRangeCoveredAsync(symbol, interval, startUtc, endUtc, cancellationToken);

        if (covered)
            return await _cache.GetCandlesInRangeAsync(symbol, interval, startUtc, endUtc, cancellationToken);

        var fresh = await _inner.GetHistoricalCandlesAsync(symbol, interval, startUtc, endUtc, cancellationToken);
        await _cache.SaveCandlesAsync(symbol, interval, startUtc, endUtc, fresh, cancellationToken);
        return fresh;
    }
}