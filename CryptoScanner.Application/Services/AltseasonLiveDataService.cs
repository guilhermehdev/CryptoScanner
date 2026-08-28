using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CryptoScanner.Core.Models.Altseason;

namespace CryptoScanner.Application.Services;

public sealed class AltseasonLiveDataService
{
    private const string StateFileName = "altseason-snapshot.json";
    private static readonly TimeSpan MinimumBaseRefresh = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinimumSecondaryRefresh = TimeSpan.FromMinutes(15);
    private static readonly SemaphoreSlim BaseRefreshLock = new(1, 1);

    private readonly HttpClient _http;
    private readonly string _stateFilePath;
    private AltseasonSnapshot? _previous;
    private decimal _previousAltVolume;
    private AltseasonSnapshot? _currentSnapshot;
    private DateTime _lastBaseRefreshUtc = DateTime.MinValue;
    private DateTime _lastSecondaryRefreshUtc = DateTime.MinValue;
    private decimal _cachedStablecoinMarketCap;
    private decimal _cachedDefiTvlChange;

    public DateTime? ReferenceTimestampUtc => _previous?.TimestampUtc;
    public AltseasonSnapshot? CurrentSnapshot => _currentSnapshot;

    public AltseasonLiveDataService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoScanner/1.0");

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CryptoScanner");
        Directory.CreateDirectory(directory);
        _stateFilePath = Path.Combine(directory, StateFileName);
        LoadState();
    }

    public async Task<(AltseasonSnapshot Snapshot, AltseasonScore Score)> GetAsync(CancellationToken ct = default)
    {
        await BaseRefreshLock.WaitAsync(ct);
        try
        {
            if (_currentSnapshot != null && DateTime.UtcNow - _lastBaseRefreshUtc < MinimumBaseRefresh)
                return (_currentSnapshot, Core.Scoring.AltseasonScorer.Calculate(_currentSnapshot));

            var global = await GetJsonAsync<GlobalResponse>("https://api.coingecko.com/api/v3/global", ct)
                ?? throw new InvalidOperationException("CoinGecko não retornou os dados globais.");

            var markets = await GetJsonAsync<List<CoinMarket>>(
                "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=100&page=1&sparkline=false&price_change_percentage=7d,30d", ct) ?? [];

            var btc = markets.FirstOrDefault(x => x.Id == "bitcoin");
            var eth = markets.FirstOrDefault(x => x.Id == "ethereum");
            var total = global.Data.TotalMarketCap.GetValueOrDefault("usd");
            var btcCap = btc?.MarketCap ?? 0m;
            var ethCap = eth?.MarketCap ?? 0m;
            var ethBtc = btc?.CurrentPrice > 0 && eth?.CurrentPrice > 0
                ? eth.CurrentPrice / btc.CurrentPrice
                : 0m;

            var alts = markets
                .Where(x => x.Id != "bitcoin" && x.Id != "ethereum" && !IsStablecoin(x))
                .ToList();

            var btc7d = btc?.Change7d ?? 0m;
            var breadth = alts.Count == 0
                ? 0m
                : alts.Count(x => x.Change7d > btc7d) * 100m / alts.Count;

            var altVolume = alts.Sum(x => x.TotalVolume ?? 0m);
            var volumeChange = _previousAltVolume <= 0m
                ? 0m
                : (altVolume - _previousAltVolume) / _previousAltVolume * 100m;

            if (_lastSecondaryRefreshUtc == DateTime.MinValue || DateTime.UtcNow - _lastSecondaryRefreshUtc >= MinimumSecondaryRefresh)
            {
                _cachedStablecoinMarketCap = await GetStablecoinMarketCapAsync(ct);
                _cachedDefiTvlChange = await GetDefiTvlChangeAsync(ct);
                _lastSecondaryRefreshUtc = DateTime.UtcNow;
            }

            var snapshot = new AltseasonSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                BtcPrice = btc?.CurrentPrice ?? 0m,
                BtcDominance = global.Data.MarketCapPercentage.GetValueOrDefault("btc"),
                EthBtc = ethBtc,
                Total3MarketCap = Math.Max(0m, total - btcCap - ethCap),
                StablecoinMarketCap = _cachedStablecoinMarketCap,
                AltcoinBreadthPercent = breadth,
                AltcoinVolumeChangePercent = volumeChange,
                DefiTvlChangePercent = _cachedDefiTvlChange,
                Previous = _previous
            };

            var score = Core.Scoring.AltseasonScorer.Calculate(snapshot);

            _previous = snapshot with { Previous = null };
            _previousAltVolume = altVolume;
            _currentSnapshot = snapshot;
            _lastBaseRefreshUtc = DateTime.UtcNow;
            SaveState();

            return (snapshot, score);
        }
        finally
        {
            BaseRefreshLock.Release();
        }
    }

    public (AltseasonSnapshot Snapshot, AltseasonScore Score)? UpdateLivePrices(decimal btcPrice, decimal ethPrice)
    {
        if (_currentSnapshot is null || btcPrice <= 0m || ethPrice <= 0m)
            return null;

        var snapshot = _currentSnapshot with
        {
            TimestampUtc = DateTime.UtcNow,
            BtcPrice = btcPrice,
            EthBtc = ethPrice / btcPrice,
            Previous = _currentSnapshot.Previous
        };

        var score = Core.Scoring.AltseasonScorer.Calculate(snapshot);
        _currentSnapshot = snapshot;
        return (snapshot, score);
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        if ((int)response.StatusCode == 429)
        {
            // Não tenta novamente imediatamente. O monitor mantém o último snapshot
            // e a próxima janela de base fará uma nova tentativa após o intervalo.
            if (_currentSnapshot != null)
                return default;

            throw new HttpRequestException("CoinGecko limitou as requisições (HTTP 429). Aguarde alguns minutos e tente novamente.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return;

            string json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state?.Snapshot == null)
                return;

            _previous = state.Snapshot with { Previous = null };
            _previousAltVolume = state.PreviousAltVolume;
        }
        catch
        {
            _previous = null;
            _previousAltVolume = 0m;
        }
    }

    private void SaveState()
    {
        try
        {
            var state = new PersistedState
            {
                Snapshot = _previous,
                PreviousAltVolume = _previousAltVolume
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
        }
    }

    private async Task<decimal> GetStablecoinMarketCapAsync(CancellationToken ct)
    {
        try
        {
            var coins = await GetJsonAsync<List<CoinMarket>>(
                "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&category=stablecoins&order=market_cap_desc&per_page=100&page=1&sparkline=false", ct) ?? [];
            return coins.Count == 0 ? _cachedStablecoinMarketCap : coins.Sum(x => x.MarketCap ?? 0m);
        }
        catch
        {
            return _cachedStablecoinMarketCap;
        }
    }

    private async Task<decimal> GetDefiTvlChangeAsync(CancellationToken ct)
    {
        try
        {
            var chains = await GetJsonAsync<List<DefiChain>>("https://api.llama.fi/v2/chains", ct) ?? [];
            var tvl = chains.Sum(x => x.Tvl);
            return tvl <= 0m ? _cachedDefiTvlChange : chains.Sum(x => x.Tvl * x.Change1d) / tvl;
        }
        catch
        {
            return _cachedDefiTvlChange;
        }
    }

    private static bool IsStablecoin(CoinMarket x) =>
        x.Id is "tether" or "usd-coin" or "dai" or "usds" or "true-usd" or "frax" or "usde";

    private sealed class PersistedState
    {
        public AltseasonSnapshot? Snapshot { get; set; }
        public decimal PreviousAltVolume { get; set; }
    }

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
