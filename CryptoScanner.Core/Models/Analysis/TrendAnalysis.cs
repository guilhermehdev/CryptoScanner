namespace CryptoScanner.Core.Models.Analysis;

public sealed class TrendAnalysis
{
    public decimal Close { get; init; }
    public decimal Ema21 { get; init; }
    public decimal Ema50 { get; init; }
    public decimal Ema200 { get; init; }
    public decimal Rsi { get; init; }
    public decimal Atr { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal Adx { get; init; }
    public int Score { get; init; }
    public int MomentumScore { get; init; }
    public int VolatilityScore { get; init; }
    public int TrendStrengthScore { get; init; }
    public string Direction { get; init; } = "LATERAL";
}
