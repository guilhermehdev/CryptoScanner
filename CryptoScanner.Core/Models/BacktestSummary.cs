using System.Collections.Generic;

namespace CryptoScanner.Core.Models;

public sealed class BacktestSummary
{
    public required int TotalTrades { get; init; }
    public required double WinRate { get; init; }
    public required decimal TotalReturnPercent { get; init; }
    public required decimal MaxDrawdownPercent { get; init; }
    public required decimal ProfitFactor { get; init; }
    public required List<BacktestTradeResult> Trades { get; init; }
    public required FilterDiagnostics Diagnostics { get; init; }
    public required List<string> SkippedSymbols { get; init; }

    // RR médio de entrada dos trades, e o Win Rate mínimo necessário pra empatar
    // nesse RR médio (fórmula 1/(1+R)). Edge = WinRate real - WinRate de equilíbrio.
    public required decimal AvgRiskRewardAtEntry { get; init; }
    public required double BreakEvenWinRate { get; init; }
    public required double Edge { get; init; }
}