namespace CryptoScanner.Core.Models;

public sealed class ScenarioResult
{
    public required string Label { get; init; }
    public required int TotalTrades { get; init; }
    public required double WinRate { get; init; }
    public required decimal TotalReturnPercent { get; init; }
    public required decimal MaxDrawdownPercent { get; init; }
    public required decimal ProfitFactor { get; init; }
}