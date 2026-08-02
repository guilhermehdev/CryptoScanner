using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface ICandleCacheRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRangeCoveredAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
    Task<List<Candle>> GetCandlesInRangeAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
    Task SaveCandlesAsync(string symbol, string interval, DateTime rangeStartUtc, DateTime rangeEndUtc, IReadOnlyList<Candle> candles, CancellationToken cancellationToken = default);
}