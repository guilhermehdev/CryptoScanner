using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface IBacktestRunResultRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string signatureHash, CancellationToken cancellationToken = default);
    Task SaveAsync(BacktestRunResult result, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacktestRunResult>> GetAllAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}