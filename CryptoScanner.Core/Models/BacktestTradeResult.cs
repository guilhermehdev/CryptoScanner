namespace CryptoScanner.Core.Models;

public sealed class BacktestTradeResult
{
    public required string Symbol { get; init; }
    public required DateTime EntryTime { get; init; }
    public required decimal EntryPrice { get; init; }
    public required DateTime ExitTime { get; init; }
    public required decimal ExitPrice { get; init; }
    public required decimal OutcomePercent { get; init; }
    public required string ExitReason { get; init; }
    public required string Signal { get; init; }
    public required decimal Score { get; init; }

    // Contexto de risco no momento da entrada — para investigar se SLs
    // batidos rápido têm relação com a distância até suporte/resistência.
    public required decimal ResistanceDistancePercent { get; init; }
    public required decimal SupportDistancePercent { get; init; }
    public required decimal RiskRewardAtEntry { get; init; }
}