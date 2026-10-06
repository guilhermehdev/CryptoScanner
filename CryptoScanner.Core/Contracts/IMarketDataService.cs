using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface IMarketDataService
{
    Task<List<Candle>> GetCandlesAsync(
        string symbol,
        string interval,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    Task<List<Candle>> GetHistoricalCandlesAsync(
        string symbol,
        string interval,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetUsdtSymbolsAsync(
        CancellationToken cancellationToken = default);

    Task<decimal> GetCurrentPriceAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    Task<MarketFlowData> GetMarketFlowDataAsync(
    string symbol,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Fonte opcional de cotações em lote. O laboratório usa esta capacidade para
/// acompanhar muitas posições sem transformar a coleta em centenas de chamadas HTTP.
/// </summary>
public interface ICurrentPriceBatchSource
{
    Task<IReadOnlyDictionary<string, decimal>> GetCurrentPricesAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default);
}
