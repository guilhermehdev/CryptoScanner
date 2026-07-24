using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface IMarketDataService
{
    Task<List<Candle>> GetCandlesAsync(
        string symbol,
        string interval,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    Task<List<string>> GetUsdtSymbolsAsync(
        CancellationToken cancellationToken = default);

    Task<decimal> GetCurrentPriceAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
