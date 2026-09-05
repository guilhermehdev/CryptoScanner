using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Utilities;
using System.Globalization;
using System.Text.Json;

namespace CryptoScanner.Exchange.Services;

public class BinanceExchangeService : IMarketDataService
{
    private readonly HttpClient _http;

    public BinanceExchangeService() : this(new HttpClient()) { }

    public BinanceExchangeService(HttpClient http) => _http = http;

    private static readonly HashSet<string> StablecoinBaseAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDC", "TUSD", "BUSD", "FDUSD", "DAI", "USDP", "PYUSD", "USDD", "GUSD", "EURI", "USTC", "EUR",
        "RLUSD", "USD1"
    };

    private const decimal StablecoinPriceLowerBound = 0.97m;
    private const decimal StablecoinPriceUpperBound = 1.03m;
    private const decimal StablecoinMaxDailyRangePercent = 1.0m;

    public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 1000, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        string json = await _http.GetStringAsync(url, cancellationToken);
        JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
        List<Candle> candles = [];

        foreach (JsonElement item in root.EnumerateArray())
        {
            candles.Add(new Candle
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).DateTime,
                Open = decimal.Parse(item[1].GetString()!, CultureInfo.InvariantCulture),
                High = decimal.Parse(item[2].GetString()!, CultureInfo.InvariantCulture),
                Low = decimal.Parse(item[3].GetString()!, CultureInfo.InvariantCulture),
                Close = decimal.Parse(item[4].GetString()!, CultureInfo.InvariantCulture),
                Volume = decimal.Parse(item[5].GetString()!, CultureInfo.InvariantCulture)
            });
        }

        return candles;
    }

    public async Task<List<string>> GetUsdtSymbolsAsync(CancellationToken cancellationToken = default)
    {
        var tradableSymbols = await GetTradableUsdtSymbolsAsync(cancellationToken);
        var stats = await GetTickerStatsAsync(cancellationToken);

        return tradableSymbols
            .Where(symbol => !IsStablecoinLike(symbol, stats))
            .OrderByDescending(symbol => stats.TryGetValue(symbol, out var s) ? s.QuoteVolume : 0m)
            .ToList();
    }

    private static bool IsStablecoinLike(string symbol, Dictionary<string, TickerStats> stats)
    {
        if (!stats.TryGetValue(symbol, out var s)) return false;
        if (s.LastPrice < StablecoinPriceLowerBound || s.LastPrice > StablecoinPriceUpperBound) return false;
        if (s.LowPrice <= 0) return false;
        decimal dailyRangePercent = (s.HighPrice - s.LowPrice) / s.LowPrice * 100m;
        return dailyRangePercent < StablecoinMaxDailyRangePercent;
    }

    private async Task<List<string>> GetTradableUsdtSymbolsAsync(CancellationToken cancellationToken)
    {
        string url = "https://api.binance.com/api/v3/exchangeInfo";
        string json = await _http.GetStringAsync(url, cancellationToken);
        JsonDocument doc = JsonDocument.Parse(json);
        List<string> result = new();

        foreach (var symbol in doc.RootElement.GetProperty("symbols").EnumerateArray())
        {
            string status = symbol.GetProperty("status").GetString() ?? "";
            string quote = symbol.GetProperty("quoteAsset").GetString() ?? "";
            string baseAsset = symbol.GetProperty("baseAsset").GetString() ?? "";
            string name = symbol.GetProperty("symbol").GetString() ?? "";

            if (status != "TRADING" || quote != "USDT") continue;
            if (StablecoinBaseAssets.Contains(baseAsset)) continue;
            result.Add(name);
        }

        return result;
    }

    private sealed record TickerStats(decimal QuoteVolume, decimal LastPrice, decimal HighPrice, decimal LowPrice);

    private async Task<Dictionary<string, TickerStats>> GetTickerStatsAsync(CancellationToken cancellationToken)
    {
        string url = "https://api.binance.com/api/v3/ticker/24hr";
        string json = await _http.GetStringAsync(url, cancellationToken);
        JsonDocument doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, TickerStats>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string symbol = item.GetProperty("symbol").GetString() ?? "";
            decimal Parse(string property) => decimal.TryParse(item.GetProperty(property).GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) ? value : 0m;
            result[symbol] = new TickerStats(Parse("quoteVolume"), Parse("lastPrice"), Parse("highPrice"), Parse("lowPrice"));
        }

        return result;
    }

    public async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.binance.com/api/v3/ticker/price?symbol={symbol}";
        string json = await _http.GetStringAsync(url, cancellationToken);
        JsonDocument doc = JsonDocument.Parse(json);
        string price = doc.RootElement.GetProperty("price").GetString() ?? "0";
        return decimal.Parse(price, CultureInfo.InvariantCulture);
    }

    public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        long intervalMs = (long)CandleIntervalHelper.ToTimeSpan(interval).TotalMilliseconds;
        long startMs = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        long endMs = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var allCandles = new List<Candle>();
        long currentStart = startMs;

        while (currentStart < endMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&startTime={currentStart}&endTime={endMs}&limit=1000";
            string json = await _http.GetStringAsync(url, cancellationToken);
            JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
            var batch = new List<Candle>();

            foreach (JsonElement item in root.EnumerateArray())
            {
                batch.Add(new Candle
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).UtcDateTime,
                    Open = decimal.Parse(item[1].GetString()!, CultureInfo.InvariantCulture),
                    High = decimal.Parse(item[2].GetString()!, CultureInfo.InvariantCulture),
                    Low = decimal.Parse(item[3].GetString()!, CultureInfo.InvariantCulture),
                    Close = decimal.Parse(item[4].GetString()!, CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(item[5].GetString()!, CultureInfo.InvariantCulture)
                });
            }

            if (batch.Count == 0) break;
            allCandles.AddRange(batch);
            long lastOpenTimeMs = new DateTimeOffset(batch[^1].OpenTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            long nextStart = lastOpenTimeMs + intervalMs;
            if (nextStart <= currentStart) break;
            currentStart = nextStart;
            if (batch.Count < 1000) break;
        }

        return allCandles;
    }

    public async Task<MarketFlowData> GetMarketFlowDataAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var takerTask = GetTakerBuyRatioAsync(symbol, cancellationToken);
        var oiTask = GetOpenInterestHistoryAsync(symbol, cancellationToken);
        var fundingTask = GetFundingRateAsync(symbol, cancellationToken);
        var candlesTask = GetPressureCandlesAsync(symbol, cancellationToken);

        await Task.WhenAll(takerTask, oiTask, fundingTask, candlesTask);
        var oi = await oiTask;

        return new MarketFlowData
        {
            TakerBuyRatio = await takerTask,
            OpenInterestChange = oi.Count >= 2 && oi[^2].Value > 0m
                ? (oi[^1].Value - oi[^2].Value) / oi[^2].Value * 100m : 0m,
            FundingRate = await fundingTask,
            PressureCandles = await candlesTask,
            OpenInterestHistory = oi
        };
    }

    private async Task<decimal> GetTakerBuyRatioAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://fapi.binance.com/futures/data/takerlongshortRatio?symbol={symbol}&period=5m&limit=1";
            string json = await _http.GetStringAsync(url, cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement items = doc.RootElement;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return 0.5m;

            JsonElement item = items[0];
            decimal buyVol = ParseDecimal(item, "buyVol");
            decimal sellVol = ParseDecimal(item, "sellVol");
            decimal total = buyVol + sellVol;
            return total > 0m ? buyVol / total : 0.5m;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return 0.5m;
        }
    }

    private async Task<IReadOnlyList<OpenInterestSample>> GetOpenInterestHistoryAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol={symbol}&period=5m&limit=9";
            string json = await _http.GetStringAsync(url, cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement items = doc.RootElement;
            return items.EnumerateArray().Select(item => new OpenInterestSample(
                item.GetProperty("timestamp").GetInt64(),
                ReadFlowDecimal(item.GetProperty("sumOpenInterest"))))
                .OrderBy(p => p.Timestamp).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<FlowCandle>> GetPressureCandlesAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            // Extra candles allow alignment when OI publication lags the latest close.
            string url = $"https://fapi.binance.com/fapi/v1/klines?symbol={symbol}&interval=5m&limit=30";
            string json = await _http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(item => new FlowCandle(
                item[0].GetInt64(), ReadFlowDecimal(item[1]), ReadFlowDecimal(item[2]),
                ReadFlowDecimal(item[3]), ReadFlowDecimal(item[4]), ReadFlowDecimal(item[5]),
                ReadFlowDecimal(item[9]))).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static decimal ReadFlowDecimal(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetDecimal() : decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);

    private async Task<decimal> GetFundingRateAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://fapi.binance.com/fapi/v1/premiumIndex?symbol={symbol}";
            string json = await _http.GetStringAsync(url, cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(json);
            return ParseDecimal(doc.RootElement, "lastFundingRate");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return 0m;
        }
    }

    private static decimal ParseDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)) return number;
        return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : 0m;
    }
}
