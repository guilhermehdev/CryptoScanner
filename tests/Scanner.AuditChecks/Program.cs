using System.Net;
using System.Text.Json;
using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Indicators.Indicators;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var rows=Enumerable.Range(0,21).Select(i=>new object[]{now-(22-i)*3600000L,"100","101","99","100",i==20?"200":"100",now-(21-i)*3600000L-1}).ToList();
rows.Add(new object[]{now,"100","101","99","100","0.01",now+3600000L});
using var http=new HttpClient(new ResponseHandler(JsonSerializer.Serialize(rows)));
var candles=await new BinanceExchangeService(http).GetCandlesAsync("TESTUSDT","1h");
Check(candles.Count==21,"Exclude incomplete current candle");
Check(candles[^1].OpenTime.Kind==DateTimeKind.Utc,"Candle timestamp has UTC kind");
Check(VolumeAnalyzer.Calculate(candles).VolumeSpike==2,"Volume compares completed candle against prior full volumes");
using var emptyHttp=new HttpClient(new ResponseHandler(JsonSerializer.Serialize(rows.TakeLast(1))));
Check((await new BinanceExchangeService(emptyHttp).GetCandlesAsync("TESTUSDT","1h")).Count==0,"No fabricated closed candles");

var series=Enumerable.Range(0,250).Select(i=>new Candle{OpenTime=DateTime.UtcNow.AddHours(i-251),Open=100,Close=100,High=101,Low=99,Volume=100}).ToList();
series.Add(new Candle{OpenTime=DateTime.UtcNow.AddHours(-1),Open=100,Close=110,High=111,Low=100,Volume=200});
var analyzer=new AssetAnalyzer();
var analysis=analyzer.Analyze("BREAKUSDT",series,series,ScanProfile.Swing,RiskCalculationMode.SwingWithPartialExits);
Check(analysis.Setup.IsBreakout && analysis.Setup.IsShortTermBreakout,"Breakout compares to levels preceding signal candle");
Check(analysis.Setup.IsConsolidating,"Breakout candle does not erase prior consolidation");
Check(analysis.Risk.Resistance>analysis.Trend.Close,"Entry trigger remains separate from future target");
Check(analysis.Risk.RiskReward==(analysis.Risk.Resistance-analysis.Trend.Close)/(analysis.Trend.Close-analysis.Risk.Support),"RR is target distance divided by stop distance");
series[^1].Close=100;
var noBreak=analyzer.Analyze("NOBREAK",series,series,ScanProfile.Swing,RiskCalculationMode.SwingWithPartialExits);
Check(!noBreak.Setup.IsBreakout,"Wick alone is not a close breakout");
var display=new AssetScore{Close=100,Resistance=110,Support=95,ResistanceDistance=10,SupportDistance=5,RiskReward=2};
display.LivePrice=112;
Check(display.Close==100&&display.RiskReward==2&&display.LivePrice==112,"Live quote does not corrupt analysis snapshot");

AssetAnalysis Fixture(decimal support,decimal rr)=>new(){Symbol="TEST",Trend=new(){Close=100,Direction="ALTA"},Volume=new(){Spike=2},Structure=new(),Candle=new(),Setup=new(){IsBreakout=true,IsConsolidating=true},Risk=new(){Mode=RiskCalculationMode.SwingBased,Support=support,Resistance=110,SupportDistancePercent=5,ResistanceDistancePercent=10,RiskReward=rr},OpportunityScore=90};
var invalid=Fixture(-1,10);
Check(EligibilityEvaluator.Evaluate(invalid,"BULL").FailedInvalidLevels && !EligibilityEvaluator.Evaluate(invalid,"BULL").IsEligible,"Invalid support cannot pass with high ratio");
var low=Fixture(95,.1m);
Check(EligibilityEvaluator.Evaluate(low,"BULL").FailedRiskReward,"Low RR still blocked without lowering threshold");
var dto=AssetScoreFactory.Create(low,"BULL",new HashSet<string>());
Check(dto.EligibilityDetails.Contains(EligibilityThresholds.Default.MinRiskReward.ToString("F2")),"Tooltip uses actual configured RR floor");
Check(dto.QualityAnalysis==dto.EligibilityDetails,"Ineligible row explains actual evaluated rules");
Console.WriteLine($"PASS: {checks} scanner audit checks");
sealed class ResponseHandler(string json):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
}
