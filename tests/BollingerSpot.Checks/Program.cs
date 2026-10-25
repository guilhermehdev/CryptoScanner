using CryptoScanner.Core.Models;
using CryptoScanner.Infrastructure.Sqlite;
using CryptoScanner.Strategies;

int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
Candle C(int index,decimal open,decimal high,decimal low,decimal close) => new()
{ OpenTime=DateTime.UnixEpoch.AddHours(index*4),Open=open,High=high,Low=low,Close=close,Volume=1 };
List<Candle> Flat(int count=20) => Enumerable.Range(0,count).Select(i=>C(i,100,101,99,100)).ToList();

var settings=BollingerSpotSettings.Default;
var strategy=new BollingerSpotStrategy("BTCUSDT",settings);
var candles=Flat();
// Sombra abaixo não vale: somente o fechamento estritamente fora cria setup.
candles.Add(C(20,100,101,80,100));
Check(strategy.ProcessClosedCandle(candles) is null,"Touching lower band does not create setup");
// Fechamento abaixo cria candidato, sem entrada imediata.
candles.Add(C(21,100,95,89,90));
var buy=strategy.ProcessClosedCandle(candles);
Check(buy is { Direction:BollingerSpotSetupDirection.Buy, High:95m, Low:89m },"Closed below lower band creates buy setup");
Check(strategy.ProcessPrice(95m).Signal is null && strategy.Position is null,"Buy does not enter at candidate high");
var entry=strategy.ProcessPrice(95.01m).Signal;
Check(entry is not null && entry.Stop==89m && entry.Target==buy!.Middle,"Buy enters only above marked high with structural levels");
Check(strategy.ProcessPrice(95.02m).Signal is null,"Repeated ticks do not duplicate buy signal");

var invalidated=new BollingerSpotStrategy("ETHUSDT",settings);
var invalidatedCandles=Flat(); invalidatedCandles.Add(C(21,100,95,89,90));
Check(invalidated.ProcessClosedCandle(invalidatedCandles) is not null && invalidated.ProcessPrice(88.99m).Signal is null && invalidated.PendingSetup is null,
    "Buy setup is invalidated when its structural stop is reached before breakout");

// Candle acima da superior cria venda pendente apenas com LONG aberta.
var sellStrategy=new BollingerSpotStrategy("BTCUSDT",settings);
sellStrategy.RestorePosition(new BollingerSpotPosition { Symbol="BTCUSDT",Settings=settings,SetupTimeUtc=DateTime.UnixEpoch,EntryPrice=96,Stop=50,Target=200 });
var sellCandles=Flat();
sellCandles.Add(C(20,100,111,105,110));
var sell=sellStrategy.ProcessClosedCandle(sellCandles);
Check(sell is { Direction:BollingerSpotSetupDirection.Sell, High:111m, Low:105m },"Closed above upper band creates sell setup");
Check(sellStrategy.ProcessPrice(105m).Exit is null,"Sell does not exit at marked low");
var sellExit=sellStrategy.ProcessPrice(104.99m).Exit;
Check(sellExit?.Reason.Contains("Perda da mínima") == true && sellStrategy.Position is null,"Sell exits long only below marked low");

Check(new BollingerSpotSettings(BollingerSpotVariant.Bollinger20x3,BollingerSpotTimeframe.Daily).Period==20 &&
      new BollingerSpotSettings(BollingerSpotVariant.Bollinger20x23,BollingerSpotTimeframe.Daily).StandardDeviations==2.3m &&
      new BollingerSpotSettings(BollingerSpotVariant.Bollinger25x3,BollingerSpotTimeframe.Daily).Period==25 &&
      new BollingerSpotSettings(BollingerSpotVariant.Bollinger25x23,BollingerSpotTimeframe.Daily).StandardDeviations==2.3m,"Four variants map to distinct parameters");

var minutes=Enumerable.Range(0,8).Select(i=>new Candle{OpenTime=DateTime.UnixEpoch.AddHours(i),Open=10+i,High=12+i,Low=9+i,Close=11+i,Volume=1}).ToList();
var fourHours=BollingerSpotCandleAggregator.AggregateUtc(minutes,BollingerSpotTimeframe.FourHours);
Check(fourHours.Count==2 && fourHours[0].OpenTime==DateTime.UnixEpoch && fourHours[0].Open==10 && fourHours[0].High==15 && fourHours[0].Low==9 && fourHours[0].Close==14,"Four-hour UTC aggregation uses OHLC");
var days=Enumerable.Range(0,30).Select(i=>new Candle{OpenTime=DateTime.UnixEpoch.AddHours(i),Open=10,High=12,Low=9,Close=11,Volume=1}).ToList();
var daily=BollingerSpotCandleAggregator.AggregateUtc(days,BollingerSpotTimeframe.Daily);
Check(daily.Count==2 && daily[0].OpenTime==DateTime.UnixEpoch && daily[0].Volume==24,"Daily UTC aggregation uses midnight buckets");

string db=Path.Combine(AppContext.BaseDirectory,$"bollinger-{Guid.NewGuid():N}.db");
try
{
 var repo=new SqliteSimulatedTradeRepository(db);await repo.InitializeAsync();
 var id=await repo.AddAsync(new SimulatedTrade{Symbol="BTCUSDT",EntryTime=DateTime.UtcNow,EntryPrice=100,StopLoss=89,TakeProfit=102,
     BollingerVariant="Bollinger20x3",BollingerPeriod=20,BollingerStandardDeviations=3,BollingerTimeframe="Daily",BollingerSetupTime=DateTime.UnixEpoch});
 var restored=(await repo.GetAllAsync()).Single(t=>t.Id==id);
 Check(restored.StopLoss==89 && restored.TakeProfit==102 && restored.BollingerPeriod==20 && restored.BollingerTimeframe=="Daily","Restored spot simulation preserves structural levels and Bollinger metadata");
}
finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();File.Delete(db);}
Console.WriteLine($"PASS: {checks} Bollinger Spot checks");
