namespace CryptoScanner.Core.Models.Altseason;

/// <summary>
/// Immutable market snapshot used by the Altseason engine and persisted for historical analysis.
/// Values are provider-agnostic so the scoring model does not depend on a data vendor.
/// </summary>
public sealed record AltseasonSnapshot
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public decimal BtcPrice { get; init; }
    public decimal BtcDominance { get; init; }
    public decimal EthBtc { get; init; }
    public decimal Total3MarketCap { get; init; }
    public decimal StablecoinMarketCap { get; init; }
    public decimal AltcoinBreadthPercent { get; init; }
    public decimal AltcoinVolumeChangePercent { get; init; }
    public decimal DefiTvlChangePercent { get; init; }
    public decimal? ExternalAltseasonIndex { get; init; }
    public AltseasonSnapshot? Previous { get; init; }
}
