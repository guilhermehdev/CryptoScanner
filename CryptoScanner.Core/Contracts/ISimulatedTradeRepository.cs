using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface ISimulatedTradeRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<int> AddAsync(SimulatedTrade trade, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SimulatedTrade>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SimulatedTrade>> GetOpenAsync(CancellationToken cancellationToken = default);
    Task CloseTradeAsync(int id, decimal exitPrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default);
    Task UpdateTradeDetailsAsync(int id, decimal takeProfit, decimal stopLoss, string note, CancellationToken cancellationToken = default);
}