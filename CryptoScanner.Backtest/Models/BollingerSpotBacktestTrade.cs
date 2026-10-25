using CryptoScanner.Core.Models;

namespace CryptoScanner.Backtest.Models;

public sealed record BollingerSpotBacktestTrade(string Symbol, BollingerSpotSettings Settings, DateTime SetupTimeUtc,
    DateTime EntryTimeUtc, decimal EntryPrice, decimal Stop, decimal Target, DateTime? ExitTimeUtc,
    decimal? ExitPrice, string ExitReason, decimal FeePercentPerOrder = 0m, decimal SlippagePercentPerOrder = 0m)
{
    public bool Closed => ExitPrice.HasValue;
    public decimal? GrossReturnPercent => ExitPrice is null ? null : (ExitPrice.Value - EntryPrice) / EntryPrice * 100m;
    public decimal TotalCostPercent => FeePercentPerOrder + SlippagePercentPerOrder;
    public decimal EntryFillPrice => EntryPrice * (1m + TotalCostPercent / 100m);
    public decimal? ExitFillPrice => ExitPrice * (1m - TotalCostPercent / 100m);
    public decimal? NetReturnPercent => ExitFillPrice is null ? null : (ExitFillPrice.Value - EntryFillPrice) / EntryFillPrice * 100m;

    // Compatibilidade com a tela anterior: agora o retorno exibido é líquido de custos.
    public decimal? ReturnPercent => NetReturnPercent;
}

public sealed record BollingerSpotBacktestCosts(decimal FeePercentPerOrder, decimal SlippagePercentPerOrder)
{
    public decimal TotalCostPercent => FeePercentPerOrder + SlippagePercentPerOrder;
    public static BollingerSpotBacktestCosts Default { get; } = new(0.10m, 0.05m);
}

public sealed record BollingerSpotVariantComparison(
    BollingerSpotSettings Settings,
    int Trades,
    int ClosedTrades,
    decimal WinRatePercent,
    decimal GrossReturnPercent,
    decimal NetReturnPercent,
    decimal? ProfitFactor,
    decimal MaxSequentialDrawdownPercent);

public sealed record BollingerSpotBacktestResult(IReadOnlyList<BollingerSpotBacktestTrade> Trades)
{
    public IReadOnlyList<BollingerSpotBacktestTrade> ClosedTrades => Trades.Where(t => t.Closed).ToArray();
    public decimal GrossReturnPercent => ClosedTrades.Sum(t => t.GrossReturnPercent ?? 0m);
    public decimal NetReturnPercent => ClosedTrades.Sum(t => t.NetReturnPercent ?? 0m);

    public BollingerSpotVariantComparison ToComparison(BollingerSpotSettings settings)
    {
        var closed = ClosedTrades.OrderBy(trade => trade.ExitTimeUtc).ToArray();
        decimal gains = closed.Where(trade => (trade.NetReturnPercent ?? 0m) > 0m).Sum(trade => trade.NetReturnPercent ?? 0m);
        decimal losses = closed.Where(trade => (trade.NetReturnPercent ?? 0m) < 0m).Sum(trade => -(trade.NetReturnPercent ?? 0m));
        decimal cumulative = 0m, peak = 0m, maxDrawdown = 0m;
        foreach (var trade in closed)
        {
            cumulative += trade.NetReturnPercent ?? 0m;
            peak = Math.Max(peak, cumulative);
            maxDrawdown = Math.Max(maxDrawdown, peak - cumulative);
        }

        return new BollingerSpotVariantComparison(
            settings,
            Trades.Count,
            closed.Length,
            closed.Length == 0 ? 0m : closed.Count(trade => (trade.NetReturnPercent ?? 0m) > 0m) * 100m / closed.Length,
            GrossReturnPercent,
            NetReturnPercent,
            losses == 0m ? null : gains / losses,
            maxDrawdown);
    }
}
