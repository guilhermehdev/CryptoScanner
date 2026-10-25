using CryptoScanner.Core.Models;

namespace CryptoScanner.Backtest.Models;

public sealed record BollingerSpotBacktestTrade(string Symbol, BollingerSpotSettings Settings, DateTime SetupTimeUtc,
    DateTime EntryTimeUtc, decimal EntryPrice, decimal Stop, decimal Target, DateTime? ExitTimeUtc,
    decimal? ExitPrice, string ExitReason)
{
    public bool Closed => ExitPrice.HasValue;
    public decimal? ReturnPercent => ExitPrice is null ? null : (ExitPrice.Value - EntryPrice) / EntryPrice * 100m;
}

public sealed record BollingerSpotBacktestResult(IReadOnlyList<BollingerSpotBacktestTrade> Trades)
{
    public IReadOnlyList<BollingerSpotBacktestTrade> ClosedTrades => Trades.Where(t => t.Closed).ToArray();
    public decimal NetReturnPercent => ClosedTrades.Sum(t => t.ReturnPercent ?? 0m);
}
