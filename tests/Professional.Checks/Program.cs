using System.Reflection;
using System.Text.Json;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Application.Services;
using CryptoScanner.Application.Models;
using CryptoScanner.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
AssetAnalysis Asset(string symbol,decimal volume=2)=>new(){Symbol=symbol,EntryStrategy=EntryStrategy.Breakout,OpportunityScore=85,
 Trend=new(){Close=100,Direction="ALTA"},Volume=new(){Spike=volume},Structure=new(),Candle=new(),Setup=new(){IsBreakout=true,IsConsolidating=true},
 Risk=new(){Mode=RiskCalculationMode.SwingWithPartialExits,Support=95,Resistance=120,TakeProfit1=112,TakeProfit3=130,SupportDistancePercent=5,ResistanceDistancePercent=20,RiskReward=4}};
var path=Path.Combine(AppContext.BaseDirectory,Guid.NewGuid()+".db");
try
{
 var repo=new SqliteSignalRepository(path);await repo.InitializeAsync();
 var scanner=new ScannerService(new Market(),repo,new Watchlist(),new AssetAnalyzer());
 var method=typeof(ScannerService).GetMethod("PersistEligibleSignalsAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
 async Task<(FilterDiagnostics Diagnostics,List<NewSignalAlert> Signals)> Persist(List<AssetAnalysis> assets,ScanProfile profile)=>
  await (Task<(FilterDiagnostics,List<NewSignalAlert>)>)method.Invoke(scanner,new object[]{assets,"BULL",profile,CancellationToken.None})!;
 var candidates=Enumerable.Range(0,35).Select(i=>Asset("COIN"+i)).ToList();candidates.Add(Asset("LOWVOL",.5m));
 var first=await Persist(candidates,ScanProfile.Swing);
 Check(first.Diagnostics.TotalAnalyzed==36 && first.Signals.Count==35,"Eligibility persists beyond visual top thirty");
 Check(first.Diagnostics.PassedAll==35 && first.Diagnostics.SignalsSaved==35,"Eligible and saved counts separated");
 Check(first.Diagnostics.OnlyBlockedBy["FailedVolumeSpike"].SequenceEqual(new[]{"LOWVOL"}),"Single-filter counterfactual identifies exact asset");
 candidates[0].OpportunityScore=65;
 var repeat=await Persist(candidates,ScanProfile.Swing);
 Check(repeat.Signals.Count==0 && repeat.Diagnostics.PassedAll==35 && repeat.Diagnostics.SkippedDuplicateToday==35,"Duplicate does not erase eligibility");
 var other=await Persist(new(){Asset("COIN0")},ScanProfile.Intraday);
 Check(other.Signals.Count==1,"Profiles do not block one another");
 var history=await repo.GetSignalsAsync();
 Check(history.All(s=>s.ExecutionJson.Length>0),"Execution state survives database round trip");
 var signal=history.First();var execution=JsonSerializer.Deserialize<LabTrade>(signal.ExecutionJson)!;
 LabSimulation.Tick(execution,112,execution.EntryMs+30000);
 await repo.UpdateExecutionAsync(signal.Id,signal.ExecutionJson,execution);
 var after=(await repo.GetSignalsAsync()).First(s=>s.Id==signal.Id);
 Check(JsonSerializer.Deserialize<LabTrade>(after.ExecutionJson)!.Stop==95,"Signal TP1 preserves stop");
 var stale=JsonSerializer.Deserialize<LabTrade>(signal.ExecutionJson)!;
 await repo.UpdateExecutionAsync(signal.Id,signal.ExecutionJson,stale);
 Check((await repo.GetSignalsAsync()).First(s=>s.Id==signal.Id).ExecutionJson==after.ExecutionJson,"Stale writer cannot revert partial exit");
 LabSimulation.Tick(execution,120,execution.EntryMs+60000);
 Check(execution.Stop==execution.EntryFill,"Signal and lab share TP2 stop rule");
 first.Diagnostics.CompletedUtc=DateTime.UtcNow;first.Diagnostics.Requested=37;first.Diagnostics.Errors["BAD"]="Insufficient data";
 await repo.SaveScanRunAsync("Swing",first.Diagnostics);
 await using(var db=new SqliteConnection($"Data Source={path}")){await db.OpenAsync();await using var cmd=db.CreateCommand();cmd.CommandText="SELECT DiagnosticsJson FROM ScanRuns";var saved=JsonSerializer.Deserialize<FilterDiagnostics>((string)(await cmd.ExecuteScalarAsync())!)!;Check(saved.Errors.ContainsKey("BAD")&&saved.OnlyBlockedBy.Count==1,"Full funnel and errors persisted");}
 var concurrent=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Persist(new(){Asset("ATOMIC")},ScanProfile.Swing)));
 Check(concurrent.Sum(r=>r.Signals.Count)==1,"Concurrent insertion atomic");
}
finally{SqliteConnection.ClearAllPools();File.Delete(path);}
Check(ExecutionCosts.NetReturn(0)<0,"Flat trade loses execution costs");
var a=new AssetScore{Score=100,BuyingPressureScore=100,Support=90,Resistance=120};long at=1_800_000;
var trade=LabSimulation.TryOpen(new("TEST","Swing",at,at,100,24,a,""){DecisionTimeMs=at},new(1,"","",0,1),10000,0,false).Trade!;
LabSimulation.Tick(trade,120,at+30000);
Check(Math.Abs(trade.NetProfit/10-ExecutionCosts.NetReturn(20))<.0000001m,"Backtest costs match laboratory single exit");
// Exercise actual backtest partial-exit routine, not a duplicate implementation.
var positionType=typeof(StrategyBacktester).GetNestedType("BacktestOpenPosition",BindingFlags.NonPublic)!;
var position=Activator.CreateInstance(positionType)!;
void Set(string name,object value)=>positionType.GetProperty(name)!.SetValue(position,value);
Set("Symbol","TEST");Set("EntryPrice",100m);Set("EntryTime",DateTime.UtcNow);Set("StopLoss",90m);Set("TakeProfit1",110m);Set("TakeProfit",120m);Set("TakeProfit3",130m);Set("Tp1Fraction",.4m);Set("Tp2Fraction",.4m);Set("RemainingFraction",1m);
var partial=typeof(StrategyBacktester).GetMethod("ProcessPartialExits",BindingFlags.NonPublic|BindingFlags.Static)!;
var results=new List<BacktestTradeResult>();
bool Tick(decimal low,decimal high)=> (bool)partial.Invoke(null,new object[]{position,new Candle{Open=105,Low=low,High=high,Close=high},DateTime.UtcNow,results})!;
Check(!Tick(100,110) && (decimal)positionType.GetProperty("StopLoss")!.GetValue(position)! ==90,"Backtest TP1 preserves stop");
Check(!Tick(111,120) && (decimal)positionType.GetProperty("StopLoss")!.GetValue(position)! ==100.05m,"Backtest TP2 moves stop");
Check(Tick(99,130)&&results.Count==1,"Stop precedes target when candle is ambiguous");
var day=new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc);
var daily=new List<Candle>{new(){OpenTime=day.AddDays(-1),Close=100},new(){OpenTime=day,Close=999}};
Check(CryptoScanner.Core.Utilities.CandleTimeline.ClosedPrefixCount(daily,0,TimeSpan.FromDays(1),day.AddHours(10))==1,"Incomplete daily candle excluded at intraday decision");
Check(CryptoScanner.Core.Utilities.CandleTimeline.ClosedPrefixCount(daily,0,TimeSpan.FromDays(1),day.AddDays(1))==2,"Daily data becomes available only at close");
var fixture=Asset("PARITY");
var live=EligibilityEvaluator.Evaluate(fixture,"BULL",ScannerProfiles.For(ScanProfile.Swing));
var historical=EligibilityEvaluator.Evaluate(fixture,"BULL",ScannerProfiles.For(ScanProfile.Swing));
Check(live.IsEligible&&historical.IsEligible,"Shared scanner preset accepts controlled strategy fixture");
var score=CryptoScanner.Strategies.OpportunityScoreCalculator.Calculate(fixture);fixture.RetailFlowScore=100;
Check(CryptoScanner.Strategies.OpportunityScoreCalculator.Calculate(fixture)==score,"New strategy score does not depend on unavailable historical futures flow");
var portfolioTrades=Enumerable.Range(0,6).Select(i=>new BacktestTradeResult{Symbol="P"+i,EntryTime=day,ExitTime=day.AddHours(1),EntryPrice=100,ExitPrice=110,OutcomePercent=10,ExitReason="TP",Signal="COMPRA",Score=80-i,ResistanceDistancePercent=10,SupportDistancePercent=5,RiskRewardAtEntry=2}).ToList();
var portfolio=BacktestPortfolioSummary.Calculate(portfolioTrades);
Check(portfolio.Accepted==5&&portfolio.Rejected==1&&portfolio.FinalCapital==10500,"Portfolio cap and capital differ from summing all six trades");
var invalid=Asset("INVALID");
var badLevels=new AssetAnalysis {Symbol="BAD",Trend=invalid.Trend,Volume=invalid.Volume,Structure=invalid.Structure,Candle=invalid.Candle,Setup=invalid.Setup,OpportunityScore=85,EntryStrategy=EntryStrategy.Breakout,
 Risk=new(){Mode=RiskCalculationMode.SwingWithPartialExits,Support=101,Resistance=120,RiskReward=4,ResistanceDistancePercent=20}};
var invalidResult=EligibilityEvaluator.Evaluate(badLevels,"BULL",ScannerProfiles.For(ScanProfile.Swing));
Check(invalidResult.FailedInvalidLevels && !invalidResult.FailedRiskReward && !invalidResult.IsEligible,"Invalid geometry separated from low RR");
var lowRr=new AssetAnalysis {Symbol="SOLV",Trend=new(){Close=.00401m,Direction="ALTA"},Volume=invalid.Volume,Structure=invalid.Structure,Candle=invalid.Candle,Setup=invalid.Setup,OpportunityScore=85,EntryStrategy=EntryStrategy.Breakout,
 Risk=new(){Mode=RiskCalculationMode.SwingWithPartialExits,Support=.003467142857142857m,Resistance=.00428m,TakeProfit1=.004172m,TakeProfit3=.00471686m,RiskReward=.497368421m,ResistanceDistancePercent=6.733167m}};
var lowResult=EligibilityEvaluator.Evaluate(lowRr,"BULL",ScannerProfiles.For(ScanProfile.Swing));
Check(lowResult.FailedRiskReward && !lowResult.FailedInvalidLevels,"Exported SOLV has valid geometry and low RR");
var metrics=EntryRiskMetrics.Calculate(110.055m,95,120);
Check(metrics.RiskReward<1 && metrics.RiskReward != 4,"Next-open gap recalculates actual entry RR");
var serialized=JsonSerializer.Serialize(ScannerProfiles.For(ScanProfile.Intraday));
var node=System.Text.Json.Nodes.JsonNode.Parse(serialized)!;node["MinimumTargetAtr"]=2m;
var atrThresholds=JsonSerializer.Deserialize<EligibilityThresholds>(node.ToJsonString())!;
Check(EligibilityEvaluator.MinimumTargetDistance(Asset("NOATR"),atrThresholds)==decimal.MaxValue,"ATR experiment rejects missing volatility");
Check(EligibilityEvaluator.MinimumTargetDistance(Asset("DEFAULT"),ScannerProfiles.For(ScanProfile.Intraday))==15,"Live distance preset remains unchanged");
var archived=new FilterDiagnostics{Thresholds=ScannerProfiles.For(ScanProfile.Swing),MarketRegime="BULL",Analyses=new(){lowRr}};
var replay=JsonSerializer.Deserialize<FilterDiagnostics>(JsonSerializer.Serialize(archived))!;
Check(EligibilityEvaluator.Evaluate(replay.Analyses[0],replay.MarketRegime,replay.Thresholds).FailedRiskReward,"Exported full analysis supports exact eligibility replay");
var buckets=new FilterDiagnostics();
StrategyDiagnosticRecorder.Record(buckets,lowRr,lowResult,day);
Check(buckets.Strategies["Breakout"].Triggered==1 && buckets.Strategies["Breakout"].Rejections["FailedRiskReward"]==1,"Triggered strategy records RR blocker");
StrategyDiagnosticRecorder.Record(buckets,fixture,new EligibilityEvaluator.EligibilityResult{FailedBreakout=true,FailedScore=true},day);
Check(buckets.Strategies["Breakout"].Evaluated==2 && buckets.Strategies["Breakout"].Triggered==1 && !buckets.Strategies["Breakout"].Rejections.ContainsKey("FailedScore"),"Non-trigger candles do not inflate candidate blockers");
var merged=new FilterDiagnostics();StrategyBacktester.MergeDiagnostics(merged,buckets);
Check(merged.Strategies["Breakout"].Samples.Count==1 && merged.Strategies["Breakout"].Samples[0].Stop==lowRr.Risk.Support,"Merging preserves rejection levels");
var excursion=new PreExitExcursion();excursion.Observe(new Candle{High=108,Low=97},100,TradeDirection.Long);
Check(excursion.FavorablePercent==8 && excursion.AdversePercent==3 && excursion.Candles==1,"Long excursion records favorable and adverse movement");
var shortExcursion=new PreExitExcursion();shortExcursion.Observe(new Candle{High=108,Low=97},100,TradeDirection.Short);
Check(shortExcursion.FavorablePercent==3 && shortExcursion.AdversePercent==8,"Short excursion reverses movement direction");
var structuralCandles=Enumerable.Range(0,30).Select(i=>new Candle{OpenTime=day.AddHours(i*4),Open=101,Close=101,High=102,Low=100}).ToList();
structuralCandles[0]=new Candle{OpenTime=day,Open=80,Close=80,High=81,Low=70};
structuralCandles[^1]=new Candle{OpenTime=day.AddHours(116),Open=101,Close=103,High=104,Low=101};
var structural=StructuralEntryExperiment.Evaluate(structuralCandles,2,true);
Check(structural.Breakout && structural.Consolidating && structural.BreakoutStop==99,"Breakout stop follows consolidation instead of distant old low");
var referenceAnalysis=new AssetAnalyzer().Analyze("BASE",structuralCandles,structuralCandles,ScanProfile.Swing,RiskCalculationMode.SwingWithPartialExits,entryStrategy:EntryStrategy.Auto);
var experimentAnalysis=new AssetAnalyzer().Analyze("EXP",structuralCandles,structuralCandles,ScanProfile.Swing,RiskCalculationMode.SwingWithPartialExits,entryStrategy:EntryStrategy.Auto,structuralEntryExperiment:true);
Check(experimentAnalysis.EntryStrategy==EntryStrategy.Breakout && experimentAnalysis.Risk.Support>referenceAnalysis.Risk.Support,"Analyzer routes experimental breakout to local invalidation");
Check(experimentAnalysis.Risk.Resistance==referenceAnalysis.Risk.Resistance,"Experiment preserves structural target");
structuralCandles[^5]=new Candle{Open=101,Close=100,High=101,Low=99.5m};
structuralCandles[^4]=new Candle{Open=100,Close=100,High=101,Low=99.7m};
structuralCandles[^3]=new Candle{Open=100,Close=100.5m,High=101,Low=100};
structuralCandles[^2]=new Candle{Open=100.5m,Close=100.5m,High=101,Low=100};
structuralCandles[^1]=new Candle{Open=100.5m,Close=101.5m,High=102,Low=100.4m};
var recovered=StructuralEntryExperiment.Evaluate(structuralCandles,2,true);
Check(recovered.Pullback && recovered.PullbackStop==98.5m,"Support touch and current recovery confirm pullback");
Check(!StructuralEntryExperiment.Evaluate(structuralCandles,2,false).Pullback,"Recovery alone does not replace uptrend");
structuralCandles[^1]=new Candle{Open=101,Close=100.6m,High=101,Low=100};
Check(!StructuralEntryExperiment.Evaluate(structuralCandles,2,true).Pullback,"Past recovery cannot authorize bearish current candle");
Check(!StructuralEntryExperiment.Evaluate(structuralCandles,0,true).Pullback,"Missing ATR cannot authorize experiment");
Check(!ScannerProfiles.For(ScanProfile.Swing).StructuralEntryExperiment,"Live scanner keeps reference strategy");
var isolatedCandles=Enumerable.Range(0,60).Select(i=>new Candle{OpenTime=day.AddHours(i*4),Open=101,Close=101,High=102,Low=100}).ToList();
isolatedCandles[20]=new Candle{Open=80,Close=80,High=81,Low=70};
isolatedCandles[^1]=new Candle{Open=102,Close=103,High=104,Low=101};
AssetAnalysis Isolated(EntryStrategy strategy,int mode)=>new AssetAnalyzer().Analyze("ISOLATED",isolatedCandles,isolatedCandles,ScanProfile.Swing,RiskCalculationMode.SwingWithPartialExits,entryStrategy:strategy,isolatedEntryExperiment:mode);
var baseline=Isolated(EntryStrategy.Breakout,0);var stopOnly=Isolated(EntryStrategy.Breakout,1);
Check(stopOnly.Risk.Support>baseline.Risk.Support && stopOnly.Setup==baseline.Setup,"Stop-only preserves complete setup and changes old support");
Check(stopOnly.Risk.Resistance==baseline.Risk.Resistance && stopOnly.Risk.TakeProfit1==baseline.Risk.TakeProfit1,"Stop-only preserves targets");
Check(Isolated(EntryStrategy.Pullback,1).Risk.Support==Isolated(EntryStrategy.Pullback,0).Risk.Support,"Stop experiment leaves pullback stop alone");
var confirmation=Isolated(EntryStrategy.Pullback,2);var pullbackBase=Isolated(EntryStrategy.Pullback,0);
Check(confirmation.Risk.Support==pullbackBase.Risk.Support && confirmation.Risk.Resistance==pullbackBase.Risk.Resistance,"Confirmation-only preserves stop and target");
Check(Isolated(EntryStrategy.Breakout,2).Setup.IsBreakout==baseline.Setup.IsBreakout,"Confirmation experiment preserves breakout trigger");
isolatedCandles[^2]=new Candle{Open=101,Close=101,High=102,Low=99};
isolatedCandles[^1]=new Candle{Open=101,Close=101,High=102,Low=100};
Check(!StructuralEntryExperiment.HasCurrentSweep(isolatedCandles),"Previous candle sweep cannot masquerade as current confirmation");
isolatedCandles[^1]=new Candle{Open=100,Close=101,High=102,Low=99};
Check(StructuralEntryExperiment.HasCurrentSweep(isolatedCandles),"Current reclaim of same reference low is accepted");
Console.WriteLine($"PASS: {count} professional checks");
sealed class Watchlist:IWatchlistRepository
{
 public Task InitializeAsync(CancellationToken token=default)=>Task.CompletedTask;
 public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
 public Task AddAsync(string symbol,CancellationToken token=default)=>Task.CompletedTask;
 public Task RemoveAsync(string symbol,CancellationToken token=default)=>Task.CompletedTask;
}
sealed class Market:IMarketDataService
{
 public Task<decimal> GetCurrentPriceAsync(string symbol,CancellationToken cancellationToken=default)=>Task.FromResult(100m);
 public Task<List<Candle>> GetCandlesAsync(string symbol,string interval,int limit=1000,CancellationToken cancellationToken=default)=>throw new NotImplementedException();
 public Task<List<string>> GetUsdtSymbolsAsync(CancellationToken cancellationToken=default)=>throw new NotImplementedException();
 public Task<List<Candle>> GetHistoricalCandlesAsync(string symbol,string interval,DateTime startUtc,DateTime endUtc,CancellationToken cancellationToken=default)=>throw new NotImplementedException();
 public Task<MarketFlowData> GetMarketFlowDataAsync(string symbol,CancellationToken cancellationToken=default)=>throw new NotImplementedException();
}
