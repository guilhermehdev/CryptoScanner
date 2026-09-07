using CryptoScanner.Core.Configuration;
namespace CryptoScanner.Core.Models;

// Price excursions in fully observed candles before the exit candle, without fees.
// A zero with Candles=0 means no eligible observation, not proof of no excursion.
public sealed class PreExitExcursion
{
    public decimal FavorablePercent { get; set; }
    public decimal AdversePercent { get; set; }
    public int Candles { get; set; }
    public void Observe(Candle candle, decimal entry, TradeDirection direction)
    {
        if(entry<=0)return;
        decimal favorable=direction==TradeDirection.Long?candle.High-entry:entry-candle.Low;
        decimal adverse=direction==TradeDirection.Long?entry-candle.Low:candle.High-entry;
        FavorablePercent=Math.Max(FavorablePercent,favorable/entry*100);
        AdversePercent=Math.Max(AdversePercent,adverse/entry*100);
        Candles++;
    }
}
