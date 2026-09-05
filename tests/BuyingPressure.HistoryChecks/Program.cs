using System.Net;
using System.Text.Json;
using CryptoScanner.Application.Services;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

var path = Path.Combine(AppContext.BaseDirectory, $"history-checks-{Guid.NewGuid():N}.db");
int checks = 0;
long end = DateTimeOffset.Parse("2026-09-05T12:00:00Z").ToUnixTimeMilliseconds();
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
async Task<object?> Scalar(string sql)
{
    await using var db = new SqliteConnection($"Data Source={path}"); await db.OpenAsync();
    await using var cmd = db.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync();
}
BuyingPressureSnapshot Snapshot(string symbol = "BTCUSDT", long? window = null, long? collected = null, decimal score = 80)
{
    long e = window ?? end;
    var result = new BuyingPressureResult(score, "fixture") { Measurements = new(e, 100, .65m, 1, 1, 2, 2, 0, 99, 1) };
    return new(symbol, e, collected ?? e + 60_000, result,
        new MarketFlowData { PressureCandles = [new(e-300_000,99,101,98,100,200,130)],
            OpenInterestHistory = [new(e-1800000,1000),new(e,1020)] });
}
SimulatedTrade Trade(long at, string symbol = "BTCUSDT") => new() { Symbol=symbol,
    EntryTime=DateTimeOffset.FromUnixTimeMilliseconds(at).UtcDateTime, EntryPrice=101, StopLoss=90, TakeProfit=120 };
try
{
    // Upgrade a database from before pressure-history support, retaining its existing trade.
    await Scalar("CREATE TABLE SimulatedTrades(Id INTEGER PRIMARY KEY AUTOINCREMENT,Symbol TEXT NOT NULL,EntryTime TEXT NOT NULL,EntryPrice REAL NOT NULL,TakeProfit REAL NOT NULL,StopLoss REAL NOT NULL,Closed INTEGER DEFAULT 0); INSERT INTO SimulatedTrades(Symbol,EntryTime,EntryPrice,TakeProfit,StopLoss) VALUES ('OLD','2026-09-04T00:00:00Z',100,120,90);");
    var trades = new SqliteSimulatedTradeRepository(path);
    await trades.InitializeAsync(); await trades.InitializeAsync();
    Check((await trades.GetAllAsync()).Single().BuyingPressureSnapshotId is null, "Migration preserves old trades without fabricated pressure");
    var repo = new SqliteBuyingPressureRepository(path);
    var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => new SqliteBuyingPressureRepository(path).SaveAsync(Snapshot()))));
    Check(ids.Distinct().Count() == 1, "Concurrent profiles deduplicate atomically");
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureSnapshots")) == 1, "One snapshot per symbol/window/version");
    await repo.SaveAsync(Snapshot(score:20, collected:end+120000));
    Check(Convert.ToDecimal(await Scalar("SELECT Score FROM BuyingPressureSnapshots")) == 80, "Readings remain immutable");
    Check(Convert.ToInt64(await Scalar("SELECT CollectedAtMs FROM BuyingPressureSnapshots")) == end+60000, "First availability timestamp retained");
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureOutcomes")) == 4, "Schedules four horizons once");
    var raw = JsonSerializer.Deserialize<MarketFlowData>((string)(await Scalar("SELECT RawDataJson FROM BuyingPressureSnapshots"))!)!;
    Check(raw.PressureCandles[0].BuyVolume == 130 && raw.OpenInterestHistory.Count == 2, "Raw inputs roundtrip");
    var metrics = JsonSerializer.Deserialize<BuyingPressureMeasurements>((string)(await Scalar("SELECT MeasurementsJson FROM BuyingPressureSnapshots"))!)!;
    Check(metrics.Atr == 1 && metrics.ReferencePrice == 100, "Reproduction references roundtrip");
    await repo.SaveAsync(Snapshot("ETHUSDT"));
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureSnapshots")) == 2, "Different assets remain distinct");
    var missing = new BuyingPressureSnapshot("MISSING",end,end+60000,BuyingPressureResult.Unavailable("OI"),new());
    await repo.SaveAsync(missing);
    Check(await Scalar("SELECT Score FROM BuyingPressureSnapshots WHERE Symbol='MISSING'") is DBNull, "Unavailable stored as null");
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureOutcomes o JOIN BuyingPressureSnapshots s ON s.Id=o.SnapshotId WHERE s.Symbol='MISSING'")) == 0, "No fabricated returns for unavailable scores");
    var entry = Trade(end+120000); int id = await trades.AddAsync(entry);
    var saved = (await trades.GetAllAsync()).Single(t=>t.Id==id);
    Check(saved.BuyingPressureSnapshotId == ids[0] && saved.BuyingPressureAtEntry == 80, "Trade linked to known pressure atomically");
    int before = await trades.AddAsync(Trade(end+30000));
    Check((await trades.GetAllAsync()).Single(t=>t.Id==before).BuyingPressureSnapshotId is null, "No lookahead to later collection");
    int stale = await trades.AddAsync(Trade(end+660000));
    Check((await trades.GetAllAsync()).Single(t=>t.Id==stale).BuyingPressureSnapshotId is null, "Stale snapshot excluded");
    await repo.SaveAsync(Snapshot(window:end+300000,collected:end+900000,score:95));
    int late = await trades.AddAsync(Trade(end+360000));
    Check((await trades.GetAllAsync()).Single(t=>t.Id==late).BuyingPressureAtEntry == 80, "Later backdated window cannot leak into earlier trade");
    int unknown = await trades.AddAsync(Trade(end+120000,"MISSING"));
    Check((await trades.GetAllAsync()).Single(t=>t.Id==unknown).BuyingPressureSnapshotId is null, "Unavailable readings not linked as valid scores");
    await repo.SaveAsync(Snapshot("MISSING",collected:end+180000));
    Check(Convert.ToDecimal(await Scalar("SELECT Score FROM BuyingPressureSnapshots WHERE Symbol='MISSING'"))==80, "A failed window can recover to its first valid reading");
    Check(Convert.ToInt64(await Scalar("SELECT CollectedAtMs FROM BuyingPressureSnapshots WHERE Symbol='MISSING'"))==end+180000, "Recovery records actual availability");
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureFailures WHERE Symbol='MISSING'"))==1, "Original failure retained after recovery");
    Check((await trades.GetAllAsync()).Single(t=>t.Id==unknown).BuyingPressureSnapshotId is null, "Recovery never retroactively links an earlier trade");
    await repo.SavePricesAsync([new("BTCUSDT",end+1800000,110,end+1860000,false)]);
    Check(Math.Abs(Convert.ToDecimal(await Scalar($"SELECT ReturnPercent FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=30"))-10)<.00001m, "Exact 30-minute return");
    await repo.SavePricesAsync([new("BTCUSDT",end+1800000,150,end+1900000,true)]);
    Check(Convert.ToDecimal(await Scalar($"SELECT Price FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=30"))==110, "Completed results immutable");
    await repo.SavePricesAsync([new("BTCUSDT",end+3300000,125,end+3360000,false)]);
    Check(await Scalar($"SELECT Price FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=60") is DBNull, "Nearby price cannot substitute for exact target");
    var reopened = new SqliteBuyingPressureRepository(path);
    var due = await reopened.GetDueTargetsAsync(end+3600000,100);
    Check(due.Any(t=>t.Symbol=="BTCUSDT" && t.CloseTimeMs==end+3600000), "Pending results survive restart");
    Check(due.All(t=>t.CloseTimeMs<=end+3600000), "No future target evaluated");
    Check((await reopened.GetDueTargetsAsync(end+3600000,100)).Count==0, "Retry cooldown prevents repeated failed requests");
    var source = new PriceSource(); var history = new BuyingPressureHistoryService(reopened,source);
    await history.CompleteDueAsync(DateTimeOffset.FromUnixTimeMilliseconds(end+3700000+300000));
    Check(Convert.ToInt64(await Scalar($"SELECT Reconstructed FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=60")) == 1, "Historical catch-up explicitly marked");
    Check(source.Requests.Count<=20, "Catch-up is bounded");
    await reopened.SavePricesAsync([new("BTCUSDT",end+14400000,90,end+14460000,true),
        new("BTCUSDT",end+86400000,120,end+86460000,true)]);
    Check(Math.Abs(Convert.ToDecimal(await Scalar($"SELECT ReturnPercent FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=240"))+10)<.00001m, "Four-hour negative return");
    Check(Math.Abs(Convert.ToDecimal(await Scalar($"SELECT ReturnPercent FROM BuyingPressureOutcomes WHERE SnapshotId={ids[0]} AND HorizonMinutes=1440"))-20)<.00001m, "24-hour positive return");
    try { await repo.SavePricesAsync([new("FUTURE",end+86400000,120,end,false)]); throw new Exception("Future price accepted"); }
    catch (ArgumentException) { checks++; }
    Check((await new SqliteSimulatedTradeRepository(path).GetAllAsync()).Single(t=>t.Id==id).BuyingPressureAtEntry==80, "Trade link survives restart and future readings");
    var historySnapshot = Snapshot("RECORD",window:end+600000);
    await history.RecordAsync("RECORD",historySnapshot.RawData,historySnapshot.Result,DateTimeOffset.FromUnixTimeMilliseconds(historySnapshot.CollectedAtMs));
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressureSnapshots WHERE Symbol='RECORD'"))==1, "Record service persists snapshots");
    Check(Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM BuyingPressurePrices WHERE Symbol='RECORD'"))==1, "Record service reuses fetched candle prices");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await repo.SaveAsync(Snapshot(),cancellation.Token); throw new Exception("Cancellation swallowed"); }
    catch (OperationCanceledException) { checks++; }
    try { await repo.SaveAsync(Snapshot() with { WindowEndMs=end+300000 }); throw new Exception("Future snapshot accepted"); }
    catch (ArgumentException) { checks++; }
    var http = new ExactPriceHandler(end+1800000);
    var exchange = new BinanceExchangeService(new HttpClient(http));
    Check(await exchange.GetFuturesCloseAsync("BTCUSDT",end+1800000)==110, "Exact futures candle parsed");
    Check(http.Url!.Contains("startTime=") && http.Url.Contains("endTime=") && http.Url.Contains("fapi/v1/klines"), "Historical query constrained to exact futures interval");
    http.WrongTime=true;
    Check(await exchange.GetFuturesCloseAsync("BTCUSDT",end+1800000) is null, "Wrong candle timestamp rejected");
    Console.WriteLine($"PASS: {checks} pressure-history integration checks");
}
finally { SqliteConnection.ClearAllPools(); File.Delete(path); }

sealed class PriceSource : IBuyingPressurePriceSource
{
    public List<PressurePriceTarget> Requests=[];
    public Task<decimal?> GetFuturesCloseAsync(string symbol,long closeTimeMs,CancellationToken token=default)
    { Requests.Add(new(symbol,closeTimeMs)); return Task.FromResult<decimal?>(105m); }
}
sealed class ExactPriceHandler(long end) : HttpMessageHandler
{
    public bool WrongTime; public string? Url;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Url=request.RequestUri!.AbsoluteUri;
        object[][] candles = [[end-300000+(WrongTime?1:0),"100","111","99","110","1",end-1]];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(candles)) });
    }
}
