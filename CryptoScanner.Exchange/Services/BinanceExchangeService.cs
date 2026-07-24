using System.Globalization;
using System.Text.Json;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Exchange.Services;

public class BinanceExchangeService : IMarketDataService
{
    private readonly HttpClient _http = new();

    public async Task<List<Candle>> GetCandlesAsync(
        string symbol,
        string interval,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        string url =
            $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";

        string json = await _http.GetStringAsync(url, cancellationToken);

        JsonElement root =
            JsonSerializer.Deserialize<JsonElement>(json);

        List<Candle> candles = [];

        foreach (JsonElement item in root.EnumerateArray())
        {
            candles.Add(new Candle
            {
                OpenTime =
                    DateTimeOffset
                        .FromUnixTimeMilliseconds(item[0].GetInt64())
                        .DateTime,

                Open =
                    decimal.Parse(item[1].GetString()!,
                        CultureInfo.InvariantCulture),

                High =
                    decimal.Parse(item[2].GetString()!,
                        CultureInfo.InvariantCulture),

                Low =
                    decimal.Parse(item[3].GetString()!,
                        CultureInfo.InvariantCulture),

                Close =
                    decimal.Parse(item[4].GetString()!,
                        CultureInfo.InvariantCulture),

                Volume =
                    decimal.Parse(item[5].GetString()!,
                        CultureInfo.InvariantCulture)
            });
        }

        return candles;
    }

    public async Task<List<string>> GetUsdtSymbolsAsync(
        CancellationToken cancellationToken = default)
    {
        string url =
            "https://api.binance.com/api/v3/exchangeInfo";

        string json =
            await _http.GetStringAsync(url, cancellationToken);

        JsonDocument doc =
            JsonDocument.Parse(json);

        List<string> result = new();

        foreach (var symbol in
                 doc.RootElement
                    .GetProperty("symbols")
                    .EnumerateArray())
        {
            string status =
                symbol.GetProperty("status")
                      .GetString() ?? "";

            string quote =
                symbol.GetProperty("quoteAsset")
                      .GetString() ?? "";

            string name =
                symbol.GetProperty("symbol")
                      .GetString() ?? "";

            if (status == "TRADING"
                && quote == "USDT")
            {
                result.Add(name);
            }
        }

        return result;
    }

    public async Task<decimal> GetCurrentPriceAsync(
    string symbol,
    CancellationToken cancellationToken = default)
    {
        string url =
            $"https://api.binance.com/api/v3/ticker/price?symbol={symbol}";

        string json =
            await _http.GetStringAsync(url, cancellationToken);

        JsonDocument doc =
            JsonDocument.Parse(json);

        string price =
            doc.RootElement
               .GetProperty("price")
               .GetString() ?? "0";

        return decimal.Parse(
            price,
            CultureInfo.InvariantCulture);
    }
}
