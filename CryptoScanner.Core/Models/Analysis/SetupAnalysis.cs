namespace CryptoScanner.Core.Models.Analysis;

public sealed class SetupAnalysis
{
    public int Score { get; init; }
    public bool IsBreakout { get; init; }
    public bool IsConsolidating { get; init; }
    public bool IsOverextended { get; init; }
    public decimal EmaDistanceAtr { get; init; }
    public decimal SwingUsageAtr { get; init; }
}
