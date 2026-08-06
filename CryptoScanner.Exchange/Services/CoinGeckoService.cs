using System.Net.Http.Headers;
using System.Text.Json;

namespace CryptoScanner.Exchange.Services;

public sealed class CoinGeckoService
{
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoScanner/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<(decimal? Dominance, string? Error)> GetBitcoinDominanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("https://api.coingecko.com/api/v3/global", cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            decimal dominance = doc.RootElement
                .GetProperty("data")
                .GetProperty("market_cap_percentage")
                .GetProperty("btc")
                .GetDecimal();

            return (dominance, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}