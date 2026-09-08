using CryptoScanner.Core.Models;
namespace CryptoScanner.Application.Services;

// Fixed research hypothesis, not optimized on historical outcomes.
public static class StructuralEntryExperiment
{
    public static bool HasCurrentSweep(IReadOnlyList<Candle> candles)
    {
        if(candles.Count<13)return false;
        decimal level=candles.Skip(candles.Count-13).Take(10).Min(c=>c.Low);
        return candles[^1].Low<level && candles[^1].Close>level;
    }
    public sealed record Result(bool Breakout, bool Consolidating, decimal BreakoutStop, bool Pullback, decimal PullbackStop);
    public static Result Evaluate(IReadOnlyList<Candle> candles, decimal atr, bool uptrend)
    {
        if(candles.Count<26 || atr<=0)return new(false,false,0,false,0);
        var signal=candles[^1];
        var range=candles.Skip(candles.Count-21).Take(20).ToArray();
        decimal low=range.Min(x=>x.Low), high=range.Max(x=>x.High);
        bool consolidating=low>0 && (high-low)/low*100<=5;
        bool breakout=consolidating && signal.Close>high;
        // Support is fixed before the five-candle correction, including the signal.
        var prior=candles.Skip(candles.Count-25).Take(20).ToArray();
        var correction=candles.Skip(candles.Count-5).ToArray();
        decimal support=prior.Min(x=>x.Low), buffer=atr*.5m;
        bool touched=correction.Any(x=>x.Low<=support+buffer && x.High>=support-buffer);
        bool declined=correction.Take(4).Any(x=>x.Close<prior[^1].Close);
        bool recovered=signal.Close>support && signal.Close>candles[^2].High && signal.Close>signal.Open;
        bool held=correction.All(x=>x.Close>=support-buffer);
        bool pullback=uptrend && support>0 && touched && declined && recovered && held;
        return new(breakout,consolidating,low-buffer,pullback,correction.Min(x=>x.Low)-buffer);
    }
}
