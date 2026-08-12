using CryptoScanner.Core.Configuration;

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

    // Fase 1 do lado de venda — não-obrigatório de propósito (default = Long), pra não
    // quebrar nenhuma construção existente de BacktestTradeResult espalhada pelo app.
    public TradeDirection Direction { get; init; } = TradeDirection.Long;

    // Contexto de risco no momento da entrada — para investigar se SLs
    // batidos rápido têm relação com a distância até suporte/resistência.
    public required decimal ResistanceDistancePercent { get; init; }
    public required decimal SupportDistancePercent { get; init; }
    public required decimal RiskRewardAtEntry { get; init; }
}