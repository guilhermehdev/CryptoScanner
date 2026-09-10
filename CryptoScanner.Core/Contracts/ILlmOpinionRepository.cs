using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface ILlmOpinionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> AddAsync(LlmOpinionRecord opinion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LlmOpinionRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);
}
