using CryptoScanner.Core.Models;
using CryptoScanner.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

string path=Path.Combine(AppContext.BaseDirectory,$"analysis-{Guid.NewGuid():N}.db");
int checks=0;
long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
void Check(bool condition,string name) { if (!condition) throw new Exception(name); checks++; }
try
{
    var repo=new SqlitePressureAnalysisRepository(path);
    var filter=new PressureAnalysisFilter(now-10000,now+1000,"BTCUSDT",30,BuyingPressureSnapshot.FormulaVersion);
    Check((await repo.LoadAsync(filter)).TotalReadings==0,"Empty database initializes safely");
    await using var db=new SqliteConnection($"Data Source={path}"); await db.OpenAsync();
    using (var tx=db.BeginTransaction())
    {
        async Task Seed(int id,decimal? score,decimal? result,long? collected=null,string symbol="BTCUSDT",string version=BuyingPressureSnapshot.FormulaVersion,bool pending=false,bool reconstructed=false)
        {
            await using var cmd=db.CreateCommand(); cmd.Transaction=tx;
            cmd.CommandText="""
                INSERT INTO BuyingPressureSnapshots(Id,Symbol,WindowEndMs,CollectedAtMs,FormulaVersion,Score,Quality,Details,ReferencePrice,RawDataJson)
                VALUES($id,$symbol,$window,$collected,$version,$score,$quality,'details',100,'{}');
                INSERT INTO BuyingPressureOutcomes(SnapshotId,HorizonMinutes,TargetTimeMs,Price,ReturnPercent,Reconstructed)
                VALUES($id,30,$due,$price,$result,$reconstructed);
                """;
            cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$symbol",symbol);
            cmd.Parameters.AddWithValue("$window",now-3600000-id*300000L);
            cmd.Parameters.AddWithValue("$collected",collected??now);
            cmd.Parameters.AddWithValue("$version",version);
            cmd.Parameters.AddWithValue("$score",score.HasValue?(object)(double)score.Value:DBNull.Value);
            cmd.Parameters.AddWithValue("$quality",score.HasValue?"Available":"Unavailable");
            cmd.Parameters.AddWithValue("$due",pending?now+3600000:now-60000);
            cmd.Parameters.AddWithValue("$price",result.HasValue?(object)(100d+(double)result.Value):DBNull.Value);
            cmd.Parameters.AddWithValue("$result",result.HasValue?(object)(double)result.Value:DBNull.Value);
            cmd.Parameters.AddWithValue("$reconstructed",reconstructed?1:0);
            await cmd.ExecuteNonQueryAsync();
        }
        await Seed(1,0,-10,collected:filter.FromMs);
        await Seed(2,9.99m,0);
        await Seed(3,10,10);
        await Seed(4,100,20,reconstructed:true);
        await Seed(5,99.99m,null,pending:true);
        await Seed(6,50,null);
        await Seed(7,null,null);
        await Seed(8,80,99,version:"other-version");
        await Seed(9,80,99,symbol:"ETHUSDT");
        await Seed(10,80,99,collected:filter.ToMs);
        tx.Commit();
    }
    var report=await repo.LoadAsync(filter);
    Check(report.TotalReadings==7 && report.Unavailable==1,"Date boundaries, asset and formula isolate population");
    Check(report.Bands.Sum(b=>b.Readings)==6,"Unavailable readings excluded from numeric bands");
    var low=report.Bands.Single(b=>b.Band==0);
    Check(low.Readings==2 && low.Evaluated==2 && low.AverageReturn==-5,"Zero and decimal boundary map correctly; flat returns included in mean");
    Check(low.PositivePercent==0 && low.MinReturn==-10 && low.MaxReturn==0,"Positive percentage and extremes use evaluated sample");
    Check(report.Bands.Single(b=>b.Band==1).PositivePercent==100,"Exact 10 goes to next band");
    var high=report.Bands.Single(b=>b.Band==9);
    Check(high.Readings==2 && high.Evaluated==1 && high.AverageReturn==20 && high.Pending==1,"100 included; pending excluded from average");
    Check(high.Reconstructed==1 && high.PositivePercent==100,"Recovered results identified and counted");
    var unevaluated=report.Bands.Single(b=>b.Band==5);
    Check(unevaluated.Overdue==1 && unevaluated.AverageReturn is null && unevaluated.PositivePercent is null,"Overdue has no invented zero return");
    Check(report.History.Single(r=>r.Id==5).Status=="Aguardando prazo","Pending history status");
    Check(report.History.Single(r=>r.Id==6).Status=="Aguardando recuperação","Overdue history status");
    Check(report.History.Single(r=>r.Id==7).Status=="Sem dados","Unavailable history status");
    Check(report.History.Single(r=>r.Id==4).Source=="Histórico recuperado","Recovered provenance shown");
    Check((await repo.LoadAsync(filter with { Symbol="btcusdt" })).TotalReadings==7,"Case-insensitive asset");
    Check((await repo.LoadAsync(filter with { Symbol="" })).TotalReadings==8,"All-assets filter");
    Check((await repo.LoadAsync(filter with { Symbol="BTCUSDT' OR 1=1 --" })).TotalReadings==0,"Parameterized filters");
    foreach(int horizon in new[]{60,240,1440})
        Check((await repo.LoadAsync(filter with { HorizonMinutes=horizon })).Bands.Sum(b=>b.Evaluated)==0,"Horizons never share other horizon returns");
    await using(var cmd=db.CreateCommand())
    {
        cmd.CommandText="""
            WITH RECURSIVE numbers(n) AS (SELECT 100 UNION ALL SELECT n+1 FROM numbers WHERE n<700)
            INSERT INTO BuyingPressureSnapshots(Id,Symbol,WindowEndMs,CollectedAtMs,FormulaVersion,Score,Quality,Details,ReferencePrice,RawDataJson)
            SELECT n,'LARGE',n*300000,$now,$version,70,'Available','bulk',100,'{}' FROM numbers;
            INSERT INTO BuyingPressureOutcomes(SnapshotId,HorizonMinutes,TargetTimeMs,Price,ReturnPercent)
            SELECT Id,30,$now-60000,101,1 FROM BuyingPressureSnapshots WHERE Symbol='LARGE';
            """;
        cmd.Parameters.AddWithValue("$now",now);cmd.Parameters.AddWithValue("$version",BuyingPressureSnapshot.FormulaVersion);
        await cmd.ExecuteNonQueryAsync();
    }
    var large=await repo.LoadAsync(filter with { Symbol="LARGE" });
    Check(large.TotalReadings==601 && large.History.Count==500,"Display capped without truncating population");
    Check(large.Bands.Single().Evaluated==601 && large.Bands.Single().AverageReturn==1,"Aggregation includes rows outside history display");
    Check(large.History.First().Id==700 && large.History.Last().Id==201,"Deterministic recent-row ordering");
    try { await repo.LoadAsync(filter with { HorizonMinutes=5 });throw new Exception("Invalid horizon accepted"); }
    catch(ArgumentException){ checks++; }
    using var cts=new CancellationTokenSource();cts.Cancel();
    try { await repo.LoadAsync(filter,cts.Token);throw new Exception("Cancellation swallowed"); }
    catch(OperationCanceledException){ checks++; }
    Console.WriteLine($"PASS: {checks} pressure-analysis checks");
}
finally { SqliteConnection.ClearAllPools();File.Delete(path); }
