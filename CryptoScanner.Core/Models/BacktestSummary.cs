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
}