namespace CryptoScanner.Core.Models.Analysis;

public sealed class VolumeAnalysis
{
    public decimal RelativeVolume { get; init; }
    public decimal BuyingVolume { get; init; }
    public decimal SellingVolume { get; init; }
    public decimal Imbalance { get; init; }
    public decimal Spike { get; init; }
    public int Score { get; init; }
    public bool IsClimax { get; init; }
    public bool HasAbsorption { get; init; }
    public bool HasDistribution { get; init; }
    public bool HasExhaustion { get; init; }
}