using CryptoScanner.Core.Models;
namespace CryptoScanner.Core.Utilities;
public static class CandleTimeline
{
    public static int ClosedPrefixCount(IReadOnlyList<Candle> candles,int start,TimeSpan interval,DateTime decisionTime)
    {
        int index=start;
        while(index<candles.Count && candles[index].OpenTime+interval<=decisionTime)index++;
        return index;
    }
}
