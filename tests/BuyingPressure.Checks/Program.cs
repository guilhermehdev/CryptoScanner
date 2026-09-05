using System.Net;
using System.Text.Json;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Strategies;

int checks = 0;
long end = DateTimeOffset.Parse("2026-09-05T12:00:00Z").ToUnixTimeMilliseconds();
var now = DateTimeOffset.FromUnixTimeMilliseconds(end + 60_000);
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}
MarketFlowData Fixture(decimal buy = .65m, decimal step = .15m, decimal oi = 2m, decimal volume = 2m)
{
    var candles = Enumerable.Range(0, 26).Select(i =>
    {
        decimal open = 100m + (i < 20 ? 0 : (i - 20) * step);
        decimal close = open + (i < 20 ? 0 : step);
        decimal v = i < 20 ? 100 : 100 * volume;
        return new FlowCandle(end - (26 - i) * 300_000L, open,
            Math.Max(open, close) + .5m, Math.Min(open, close) - .5m, close, v, v * (i < 20 ? .5m : buy));
    }).ToArray();
    return new() { PressureCandles = candles, OpenInterestHistory = Enumerable.Range(0, 9)
        .Select(i => new OpenInterestSample(end - (8 - i) * 300_000L, 1000m * (1m + (i - 2) * oi / 600m))).ToArray() };
}
MarketFlowData WithCandles(MarketFlowData f, IEnumerable<FlowCandle> c) => new()
    { PressureCandles = c.ToArray(), OpenInterestHistory = f.OpenInterestHistory };
decimal? Score(MarketFlowData f) => BuyingPressureCalculator.Calculate(f, now).Score;
var strong = Fixture();
Check(Score(strong) > 70 && Score(strong) <= 100, "Confirmed buying scores strongly");
Check(Score(Fixture(.5m, 0, 0, 1)) == 50, "Neutral market is 50");
Check(Score(Fixture(.35m, -.15m)) < 50, "Selling with declining price scores below neutral");
Check(Score(Fixture(.7m, -.15m)) <= 50, "Absorbed buying without price response cannot score high");
Check(Score(Fixture(.4m)) <= 50, "Rising price without aggressive buying cannot score high");
Check(Score(Fixture(oi: -2m)) <= 75 && Score(Fixture(oi: -2m)) < Score(strong), "Falling OI weakens confirmation");
Check(Score(Fixture(volume: 1m)) < Score(strong), "Volume expansion confirms buying");
Check(Score(Fixture(step: 2m)) < Score(strong), "Overextended price is penalized");
Check(Score(new MarketFlowData()) is null, "Missing data is unavailable, not 50");
Check(Score(new MarketFlowData { PressureCandles = strong.PressureCandles }) is null, "Missing OI is unavailable");
Check(BuyingPressureCalculator.Calculate(strong, now.AddMinutes(20)).Score is null, "Stale data unavailable");
Check(Score(WithCandles(strong, strong.PressureCandles.Skip(1))) is null, "Insufficient history unavailable");
Check(Score(WithCandles(strong, strong.PressureCandles.Where((_, i) => i != 12))) is null, "Gap unavailable");
Check(Score(WithCandles(strong, strong.PressureCandles.Select((c, i) => i == 25 ? c with { Volume = 0 } : c))) is null, "Zero volume rejected");
Check(Score(WithCandles(strong, strong.PressureCandles.Select((c, i) => i == 25 ? c with { BuyVolume = c.Volume + 1 } : c))) is null, "Invalid buy volume rejected");
var openCandle = strong.PressureCandles[^1] with { OpenTime = end, Close = 500, High = 501 };
Check(Score(WithCandles(strong, strong.PressureCandles.Append(openCandle))) == Score(strong), "Unfinished candle ignored");
Check(Score(WithCandles(strong, strong.PressureCandles.Reverse())) == Score(strong), "Response order independent");
var alternating = WithCandles(strong, strong.PressureCandles.Select((c, i) => i < 20 ? c :
    c with { BuyVolume = c.Volume * (i % 2 == 0 ? .85m : .45m) }));
Check(Score(alternating) < Score(strong), "Sustained buying beats alternating buying at same average");
foreach (decimal b in new[] { 0m, .3m, .5m, .8m, 1m })
foreach (decimal s in new[] { -2m, 0m, .2m, 2m })
    Check(Score(Fixture(b, s)) is >= 0 and <= 100, "Score remains bounded");

// Exercise actual transport parsing without depending on live market/network availability.
string klines = JsonSerializer.Serialize(strong.PressureCandles.Select(c => new object[]
    { c.OpenTime, c.Open.ToString(System.Globalization.CultureInfo.InvariantCulture), c.High, c.Low, c.Close, c.Volume,
      c.OpenTime + 299_999, 0, 0, c.BuyVolume, 0, 0 }));
string oiJson = JsonSerializer.Serialize(strong.OpenInterestHistory.Select(p => new { timestamp = p.Timestamp, sumOpenInterest = p.Value }));
var handler = new FixtureHandler(klines, oiJson);
var service = new BinanceExchangeService(new HttpClient(handler));
var parsed = await service.GetMarketFlowDataAsync("TESTUSDT");
Check(Score(parsed) == Score(strong), "API fields feed the actual calculator");
Check(handler.Paths.Any(p => p.Contains("fapi/v1/klines") && p.Contains("interval=5m")), "Uses futures klines");
Check(parsed.TakerBuyRatio == .6m && parsed.FundingRate == .0001m, "Legacy flow fields retained");
handler.Klines = "[]";
Check(Score(await service.GetMarketFlowDataAsync("TESTUSDT")) is null, "Empty API response unavailable");
handler.Klines = "broken json";
Check(Score(await service.GetMarketFlowDataAsync("TESTUSDT")) is null, "Malformed response unavailable");
handler.Klines = klines; handler.FailKlines = true;
parsed = await service.GetMarketFlowDataAsync("TESTUSDT");
Check(Score(parsed) is null && parsed.TakerBuyRatio == .6m, "Kline failure isolated from legacy score");
handler.FailKlines = false; handler.Oi = "[]";
Check(Score(await service.GetMarketFlowDataAsync("TESTUSDT")) is null, "Missing OI API response unavailable");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
try { await service.GetMarketFlowDataAsync("TESTUSDT", cancel.Token); throw new Exception("Cancellation swallowed"); }
catch (OperationCanceledException) { checks++; }
Console.WriteLine($"PASS: {checks} buying-pressure regression checks");

sealed class FixtureHandler(string klines, string oi) : HttpMessageHandler
{
    public string Klines = klines, Oi = oi;
    public bool FailKlines;
    public List<string> Paths = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string path = request.RequestUri!.PathAndQuery;
        Paths.Add(path);
        if (path.Contains("/klines") && FailKlines)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        string json = path.Contains("/klines") ? Klines : path.Contains("openInterestHist") ? Oi :
            path.Contains("premiumIndex") ? "{\"lastFundingRate\":\"0.0001\"}" : "[{\"buyVol\":\"60\",\"sellVol\":\"40\"}]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
