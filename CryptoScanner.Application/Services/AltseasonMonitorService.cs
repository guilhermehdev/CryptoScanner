using CryptoScanner.Core.Models.Altseason;
using CryptoScanner.Core.Scoring;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Application facade for calculating the Altseason score. Data acquisition remains
/// outside this class so the same engine can be fed by live APIs or historical backtests.
/// </summary>
public sealed class AltseasonMonitorService
{
    public AltseasonScore Analyze(AltseasonSnapshot snapshot)
        => AltseasonScorer.Calculate(snapshot);

    public AltseasonScore Analyze(
        DateTime timestampUtc,
        decimal btcPrice,
        decimal btcDominance,
        decimal ethBtc,
        decimal total3MarketCap,
        decimal stablecoinMarketCap,
        decimal altcoinBreadthPercent,
        decimal altcoinVolumeChangePercent,
        decimal defiTvlChangePercent,
        decimal? externalAltseasonIndex = null,
        AltseasonSnapshot? previous = null)
        => Analyze(new AltseasonSnapshot
        {
            TimestampUtc = timestampUtc,
            BtcPrice = btcPrice,
            BtcDominance = btcDominance,
            EthBtc = ethBtc,
            Total3MarketCap = total3MarketCap,
            StablecoinMarketCap = stablecoinMarketCap,
            AltcoinBreadthPercent = altcoinBreadthPercent,
            AltcoinVolumeChangePercent = altcoinVolumeChangePercent,
            DefiTvlChangePercent = defiTvlChangePercent,
            ExternalAltseasonIndex = externalAltseasonIndex,
            Previous = previous
        });
}
