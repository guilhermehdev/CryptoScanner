namespace CryptoScanner.Core.Contracts;

public interface IWatchlistRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(string symbol, CancellationToken cancellationToken = default);
    Task RemoveAsync(string symbol, CancellationToken cancellationToken = default);
}