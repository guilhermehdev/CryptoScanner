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

    /// <summary>
    /// Salva o progresso da saída parcial (TP1/TP2 batidos, fração restante, soma ponderada
    /// dos resultados já realizados, e o stop — que pode ter se movido pro breakeven).
    /// Chamado toda vez que um alvo intermediário é atingido, sem fechar o trade por completo.
    /// </summary>
    Task UpdatePartialExitStateAsync(
        int id, bool tp1Hit, bool tp2Hit, decimal remainingFraction, decimal weightedExitSum, decimal stopLoss,
        CancellationToken cancellationToken = default);
}