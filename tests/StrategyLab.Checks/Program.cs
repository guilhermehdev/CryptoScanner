using System.Text.Json;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Application.Services;
using CryptoScanner.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
AssetScore Asset(string symbol="BTCUSDT",decimal? pressure=70)=>new(){Symbol=symbol,Score=60,BuyingPressureScore=pressure,Close=100,Support=90,Resistance=120,TakeProfit1=110,TakeProfit3=130,Rsi=45,VolumeSpike=2,MarketRegime="BULL"};
LabOpportunity Opportunity(string symbol="BTCUSDT",long time=1800000,decimal? pressure=70)
{var a=Asset(symbol,pressure);return new(symbol,"Swing",time,time+1000,100,240,a,JsonSerializer.Serialize(a)){DecisionTimeMs=time+1000};}
LabTrade Open(int variant=1)=>LabSimulation.TryOpen(Opportunity(),LabParameters.Initial[variant-1],10000,0,false).Trade!;
var t=Open();
Check(t.EntryFill==100.05m && t.Quantity*t.EntryFill*(1+t.FeeRate)==1000,"Entry applies slippage and fee within ticket");
Check(LabParameters.Initial.Select(p=>p.Id).Distinct().Count()==5,"Five stable variants");
Check(LabSimulation.TryOpen(Opportunity(),LabParameters.Initial[0],999,0,false).Trade is null,"Capital limit");
Check(LabSimulation.TryOpen(Opportunity(),LabParameters.Initial[0],10000,5,false).Trade is null,"Position limit");
Check(LabSimulation.TryOpen(Opportunity(),LabParameters.Initial[0],10000,0,true).Trade is null,"Duplicate symbol limit");
Check(LabSimulation.TryOpen(Opportunity(pressure:null),LabParameters.Initial[0],10000,0,false).Trade is null,"Missing pressure rejected");
Check(LabSimulation.TryOpen(Opportunity() with {Quote=89},LabParameters.Initial[0],10000,0,false).Trade is null,"Entry below stop rejected");
Check(LabSimulation.TryOpen(Opportunity() with {DecisionTimeMs=1900000},LabParameters.Initial[0],10000,0,false).Trade is null,"Stale quote rejected");
Check(LabSimulation.TryOpen(Opportunity(pressure:45),LabParameters.Initial[0],10000,0,false).Trade is null && LabSimulation.TryOpen(Opportunity(pressure:45),LabParameters.Initial[1],10000,0,false).Trade is not null,"Pressure mutation explores different acceptance");
long at=t.EntryMs;
var exits=LabSimulation.Tick(t,110,at+30000);
Check(exits.Count==1 && t.Tp1Hit && t.Stop==90 && t.Remaining==.6m,"TP1 partial preserves stop");
Check(LabSimulation.Tick(t,110,at+30000).Count==0,"Repeated timestamp cannot duplicate exits");
LabSimulation.Tick(t,100,at+60000);Check(!t.Closed,"Return to entry after TP1 stays open");
LabSimulation.Tick(t,120,at+90000);Check(t.Tp2Hit && t.Stop==t.EntryFill && t.Remaining==.2m,"TP2 moves stop to entry");
LabSimulation.Tick(t,100,at+120000);Check(t.Closed && t.ExitReason=="SL" && t.NetProfit>0,"Partial profits retained on subsequent stop");
Check(LabSimulation.Tick(t,140,at+150000).Count==0,"Closed trade immutable");
t=Open();exits=LabSimulation.Tick(t,140,t.EntryMs+30000);
Check(exits.Count==3 && t.Closed && t.Remaining==0 && t.NetProfit>170 && t.NetProfit<180,"One tick crosses all targets with costs");
t=Open();exits=LabSimulation.Tick(t,80,t.EntryMs+30000);Check(exits.Single().FillPrice==79.96m && t.NetProfit< -200,"Gap through stop fills at observed adverse price");
t=Open();LabSimulation.Tick(t,100,t.DeadlineMs);Check(t.Closed&&t.ExitReason=="Prazo"&&t.HasObservationGap,"Timeout and observation gap recorded");
var narrow=Open(4);var wide=Open(5);LabSimulation.Tick(narrow,91,narrow.EntryMs+30000);LabSimulation.Tick(wide,91,wide.EntryMs+30000);
Check(narrow.Closed&&!wide.Closed,"Stop mutations produce different outcomes on same price");

string path=Path.Combine(AppContext.BaseDirectory,$"lab-{Guid.NewGuid():N}.db");
try
{
    var repo=new SqliteStrategyLabRepository(path);
    var initial=await repo.ReportAsync();Check(initial.Enabled&&initial.Variants.Count==5&&initial.Variants.All(v=>v.Cash==10000&&v.Equity==10000),"Equal initial portfolios");
    await Task.WhenAll(Enumerable.Range(0,6).Select(_=>Task.Run(()=>new SqliteStrategyLabRepository(path).ObserveAsync(Opportunity()))));
    var report=await repo.ReportAsync();Check(report.Opportunities==1&&report.Trades.Count==5&&report.Decisions.Count==5,"Concurrent duplicate observations create one common candidate");
    Check(report.Variants.All(v=>v.Cash==9000&&v.Open==1),"Equal capital debited once");
    await repo.ObserveAsync(Opportunity(time:2100000));
    report=await repo.ReportAsync();Check(report.Trades.Count==5&&report.Decisions.Count(d=>!d.Accepted)==5,"Repeated symbol rejected across opportunities");
    for(int i=0;i<6;i++)await repo.ObserveAsync(Opportunity("COIN"+i+"USDT",2400000+i*300000));
    report=await repo.ReportAsync();Check(report.Variants.All(v=>v.Open==5&&v.Cash==5000),"Portfolio limits enforced persistently");
    Check(report.Decisions.Any(d=>d.Reason=="Limite de posições"),"Rejection reasons recorded");
    await repo.TickAsync(new Dictionary<string,decimal>{{"BTCUSDT",110}},5000000);
    report=await repo.ReportAsync();Check(report.Variants.All(v=>v.Cash>5000)&&report.Trades.Where(x=>x.Symbol=="BTCUSDT").All(x=>x.Remaining==.6m),"Partial proceeds saved once");
    decimal cash=report.Variants[0].Cash;
    await repo.TickAsync(new Dictionary<string,decimal>{{"BTCUSDT",110}},5000000);
    Check((await repo.ReportAsync()).Variants[0].Cash==cash,"Duplicate persisted tick idempotent");
    await repo.SetEnabledAsync(false);
    await repo.TickAsync(new Dictionary<string,decimal>{{"BTCUSDT",130}},5030000);
    report=await repo.ReportAsync();Check(report.Trades.Where(x=>x.Symbol=="BTCUSDT").All(x=>x.Closed)&&report.Variants.All(v=>v.Closed==1),"Paused entries still evaluate open trades");
    await repo.ObserveAsync(Opportunity("PAUSED",5400000));
    report=await repo.ReportAsync();Check(report.Decisions.Count(d=>d.Reason=="Novas entradas pausadas")==5,"Pause recorded as decision");
    var restarted=new SqliteStrategyLabRepository(path);var restartReport=await restarted.ReportAsync();
    Check(!restartReport.Enabled&&restartReport.Variants[0].Cash==report.Variants[0].Cash&&restartReport.Trades.Count==report.Trades.Count,"Restart preserves portfolios, pause and trades");
    Check(restartReport.Variants.All(v=>v.Drawdown>0&&v.Gaps>0),"Observed drawdown and gaps visible");
    await restarted.SetEnabledAsync(true);await restarted.ObserveAsync(Opportunity("NEW",5700000));
    Check((await restarted.ReportAsync()).Trades.Any(x=>x.Symbol=="NEW"),"Resume starts new opportunities");
    await using(var db=new SqliteConnection($"Data Source={path}"))
    {
        await db.OpenAsync();await using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='SimulatedTrades'";
        Check(Convert.ToInt64(await cmd.ExecuteScalarAsync())==0,"Manual trade storage untouched");
        cmd.CommandText="SELECT COUNT(*) FROM LabExits";Check(Convert.ToInt64(await cmd.ExecuteScalarAsync())==15,"Every partial exit retained");
    }
    using var cts=new CancellationTokenSource();cts.Cancel();
    try{await restarted.ReportAsync(cts.Token);throw new Exception("Cancellation swallowed");}catch(OperationCanceledException){checks++;}

    // Service captures only supplied grid members and freezes their indicators before awaiting prices.
    var memory=new MemoryRepository();var market=new Market();var service=new StrategyLabService(memory,market);
    var selected=Asset("GRIDUSDT");var operation=service.ObserveGridAsync([selected],ScanProfile.Intraday);
    selected.Close=999;market.Price.SetResult(100);await operation;
    Check(memory.Records.Count==1&&memory.Records[0].Symbol=="GRIDUSDT"&&memory.Records[0].Asset.Close==100,"Only frozen visible grid candidates captured");
    Check(memory.Records[0].HoldingHours==24,"Profile holding horizon preserved");
    await service.ObserveGridAsync([selected],ScanProfile.Intraday);Check(memory.Records.Count==1&&market.Calls==1,"Repeated grid window avoids duplicate quote request");
    await service.EvaluateAsync();Check(memory.Ticks.Count==1&&memory.Ticks[0].ContainsKey("GRIDUSDT"),"Open positions evaluated independently of current grid");
    Console.WriteLine($"PASS: {checks} strategy-lab checks");
}
finally{SqliteConnection.ClearAllPools();File.Delete(path);}

sealed class MemoryRepository:IStrategyLabRepository
{
    public List<LabOpportunity> Records=[];public List<IReadOnlyDictionary<string,decimal>> Ticks=[];
    public Task<IReadOnlySet<string>> ObservedSymbolsAsync(string p,long b,CancellationToken t=default)=>Task.FromResult<IReadOnlySet<string>>(Records.Select(x=>x.Symbol).ToHashSet());
    public Task ObserveAsync(LabOpportunity o,CancellationToken t=default){Records.Add(o);return Task.CompletedTask;}
    public Task<IReadOnlyList<string>> OpenSymbolsAsync(CancellationToken t=default)=>Task.FromResult<IReadOnlyList<string>>(["GRIDUSDT"]);
    public Task TickAsync(IReadOnlyDictionary<string,decimal> p,long at,CancellationToken t=default){Ticks.Add(p);return Task.CompletedTask;}
    public Task<LabReport> ReportAsync(CancellationToken t=default)=>throw new NotSupportedException();
    public Task SetEnabledAsync(bool e,CancellationToken t=default)=>Task.CompletedTask;
}
sealed class Market:IMarketDataService
{
    public TaskCompletionSource<decimal> Price=new();public int Calls;
    public Task<decimal> GetCurrentPriceAsync(string s,CancellationToken t=default){Calls++;return Price.Task;}
    public Task<List<Candle>> GetCandlesAsync(string s,string i,int l=1000,CancellationToken t=default)=>throw new NotSupportedException();
    public Task<List<Candle>> GetHistoricalCandlesAsync(string s,string i,DateTime a,DateTime b,CancellationToken t=default)=>throw new NotSupportedException();
    public Task<List<string>> GetUsdtSymbolsAsync(CancellationToken t=default)=>throw new NotSupportedException();
    public Task<MarketFlowData> GetMarketFlowDataAsync(string s,CancellationToken t=default)=>throw new NotSupportedException();
}
