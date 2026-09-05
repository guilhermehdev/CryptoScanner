using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface IBuyingPressureRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> SaveAsync(BuyingPressureSnapshot snapshot, CancellationToken cancellationToken = default);
    Task SavePricesAsync(IReadOnlyList<PressurePrice> prices, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PressurePriceTarget>> GetDueTargetsAsync(long nowMs, int limit, CancellationToken cancellationToken = default);
}

public interface IBuyingPressurePriceSource
{
    Task<decimal?> GetFuturesCloseAsync(string symbol, long closeTimeMs, CancellationToken cancellationToken = default);
}
