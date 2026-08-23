using System.Collections.Generic;

namespace CryptoScanner.Core.Models;

/// <summary>
/// Fase 3 do roadmap (Aprendizado), Passo 2. Paralelo a PerformanceReport, mas alimentado
/// por trades do Backtest em vez de SignalHistory — amostra muito maior (centenas de trades
/// por teste, contra dezenas de sinais reais crescendo devagar). Sem "ByProfile" (backtest é
/// sempre 1 perfil por teste); com "ByDirection" no lugar (Long/Short) e "ByRiskReward"
/// (novo, informado pela investigação de RSI de 16-20/08/2026).
/// </summary>
public sealed class BacktestPerformanceReport
{
    public required int TotalTrades { get; init; }
    public required List<PerformanceBucket> ByScore { get; init; }
    public required List<PerformanceBucket> ByRsi { get; init; }
    public required List<PerformanceBucket> ByAdx { get; init; }
    public required List<PerformanceBucket> ByAtrPercent { get; init; }
    public required List<PerformanceBucket> ByRiskReward { get; init; }
    public required List<PerformanceBucket> ByPattern { get; init; }
    public required List<PerformanceBucket> BySmartMoney { get; init; }
    public required List<PerformanceBucket> ByBreakoutSource { get; init; }
    public required List<PerformanceBucket> ByMarketRegime { get; init; }
    public required List<PerformanceBucket> ByDirection { get; init; }
    public required List<PerformanceBucket> ByExitReason { get; init; }
}