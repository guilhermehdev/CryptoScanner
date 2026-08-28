using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CryptoScanner.Core.Models.Altseason;

namespace CryptoScanner.Application.Services;

public sealed class AltseasonLiveDataService
{
    private readonly HttpClient _http;
    private AltseasonSnapshot? _previous;
    private decimal _previousAltVolume;

    public AltseasonLiveDataService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoScanner/1.0");
    }

    public async Task<(AltseasonSnapshot Snapshot, AltseasonScore Score)> GetAsync(CancellationToken ct = default)
    {
        var global = await _http.GetFromJsonAsync<GlobalResponse>("https://api.coingecko.com/api/v3/global", ct)
            ?? throw new InvalidOperationException("CoinGecko não retornou os dados globais.");
        var markets = await _http.GetFromJsonAsync<List<CoinMarket>>(
            "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=100&page=1&sparkline=false&price_change_percentage=7d,30d", ct) ?? [];

        var btc = markets.FirstOrDefault(x => x.Id == "bitcoin");
        var eth = markets.FirstOrDefault(x => x.Id == "ethereum");
        var total = global.Data.TotalMarketCap.GetValueOrDefault("usd");
        var btcCap = btc?.MarketCap ?? 0m;
        var ethCap = eth?.MarketCap ?? 0m;
        var ethBtc = btc?.CurrentPrice > 0 && eth?.CurrentPrice > 0 ? eth.CurrentPrice / btc.CurrentPrice : 0m;
        var alts = markets.Where(x => x.Id != "bitcoin" && x.Id != "ethereum" && !IsStablecoin(x)).ToList();
        var btc7d = btc?.Change7d ?? 0m;
        var breadth = alts.Count == 0 ? 0m : alts.Count(x => x.Change7d > btc7d) * 100m / alts.Count;
        var altVolume = alts.Sum(x => x.TotalVolume ?? 0m);
        var volumeChange = _previousAltVolume <= 0m ? 0m : (altVolume - _previousAltVolume) / _previousAltVolume * 100m;
        var stablecoins = await GetStablecoinMarketCapAsync(ct);
        var defiChange = await GetDefiTvlChangeAsync(ct);

        var snapshot = new AltseasonSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            BtcPrice = btc?.CurrentPrice ?? 0m,
            BtcDominance = global.Data.MarketCapPercentage.GetValueOrDefault("btc"),
            EthBtc = ethBtc,
            Total3MarketCap = Math.Max(0m, total - btcCap - ethCap),
            StablecoinMarketCap = stablecoins,
            AltcoinBreadthPercent = breadth,
            AltcoinVolumeChangePercent = volumeChange,
            DefiTvlChangePercent = defiChange,
            Previous = _previous
        };

        var score = Core.Scoring.AltseasonScorer.Calculate(snapshot);
        _previous = snapshot;
        _previousAltVolume = altVolume;
        return (snapshot, score);
    }

    private async Task<decimal> GetStablecoinMarketCapAsync(CancellationToken ct)
    {
        try
        {
            var coins = await _http.GetFromJsonAsync<List<CoinMarket>>(
                "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&category=stablecoins&order=market_cap_desc&per_page=100&page=1&sparkline=false", ct) ?? [];
            return coins.Sum(x => x.MarketCap ?? 0m);
        }
        catch { return 0m; }
    }

    private async Task<decimal> GetDefiTvlChangeAsync(CancellationToken ct)
    {
        try
        {
            var chains = await _http.GetFromJsonAsync<List<DefiChain>>("https://api.llama.fi/v2/chains", ct) ?? [];
            var tvl = chains.Sum(x => x.Tvl);
            return tvl <= 0m ? 0m : chains.Sum(x => x.Tvl * x.Change1d) / tvl;
        }
        catch { return 0m; }
    }

    private static bool IsStablecoin(CoinMarket x) => x.Id is "tether" or "usd-coin" or "dai" or "usds" or "true-usd" or "frax" or "usde";

    private sealed class GlobalResponse { public GlobalData Data { get; set; } = new(); }
    private sealed class GlobalData
    {
        [JsonPropertyName("total_market_cap")] public Dictionary<string, decimal> TotalMarketCap { get; set; } = [];
        [JsonPropertyName("market_cap_percentage")] public Dictionary<string, decimal> MarketCapPercentage { get; set; } = [];
    }
    private sealed class CoinMarket
    {
        public string Id { get; set; } = "";
        [JsonPropertyName("current_price")] public decimal CurrentPrice { get; set; }
        [JsonPropertyName("market_cap")] public decimal? MarketCap { get; set; }
        [JsonPropertyName("total_volume")] public decimal? TotalVolume { get; set; }
        [JsonPropertyName("price_change_percentage_7d_in_currency")] public decimal Change7d { get; set; }
    }
    private sealed class DefiChain
    {
        public decimal Tvl { get; set; }
        [JsonPropertyName("change_1d")] public decimal Change1d { get; set; }
    }
}
