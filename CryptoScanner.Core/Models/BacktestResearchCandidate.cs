using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

/// <summary>
/// Observação histórica de um gatilho antes de ser promovido a estratégia. Não é uma
/// operação simulada: os retornos são movimentos direcionais brutos após a abertura
/// seguinte e servem somente para pesquisa de padrões.
/// </summary>
public sealed class BacktestResearchCandidate
{
    public required string Symbol { get; init; }
    public required TradeDirection Direction { get; init; }
    public required EntryStrategy Strategy { get; init; }
    public required DateTime DecisionTime { get; init; }
    public required DateTime EntryTime { get; init; }
    public required decimal EntryPrice { get; init; }
    public required string MarketRegime { get; init; }
    public required bool PassedAllFilters { get; init; }
    public required string Failures { get; init; }
    public required decimal Score { get; init; }
    public required decimal Rsi { get; init; }
    public required decimal Adx { get; init; }
    public required decimal AtrPercent { get; init; }
    public required decimal VolumeSpike { get; init; }
    public required decimal VolumeImbalance { get; init; }
    public required DateTime CloseAfter6Hours { get; init; }
    public required decimal ReturnAfter6HoursPercent { get; init; }
    public required DateTime CloseAfter24Hours { get; init; }
    public required decimal ReturnAfter24HoursPercent { get; init; }
    public required decimal MaximumFavorable24HoursPercent { get; init; }
    public required decimal MaximumAdverse24HoursPercent { get; init; }
}
