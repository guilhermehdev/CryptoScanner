using CryptoScanner.Backtest.Models;
using CryptoScanner.Core.Models;
using CryptoScanner.Strategies;

namespace CryptoScanner.Backtest.Services;

public sealed class BollingerSpotBacktester
{
    public BollingerSpotBacktestResult Run(string symbol, IReadOnlyList<Candle> candles, BollingerSpotSettings settings)
    {
        var ordered = candles.OrderBy(c => c.OpenTime).ToList();
        var strategy = new BollingerSpotStrategy(symbol, settings);
        var closed = new List<Candle>();
        var trades = new List<BollingerSpotBacktestTrade>();
        BollingerSpotBacktestTrade? open = null;

        foreach (var candle in ordered)
        {
            // O candle é confirmado antes de virar candidato. Nunca há entrada no candle candidato.
            closed.Add(candle);
            strategy.ProcessClosedCandle(closed);

            // Para candles posteriores, usa o extremo que confirma o rompimento. Caso uma barra
            // toque stop e rompimento, o stop tem prioridade conservadora.
            if (open is not null && candle.Low <= open.Stop)
            {
                var (_, exit) = strategy.ProcessPrice(open.Stop);
                if (exit is not null)
                {
                    trades[^1] = open with { ExitTimeUtc = candle.OpenTime, ExitPrice = open.Stop, ExitReason = exit.Reason };
                    open = null;
                }
                continue;
            }
            var (signal, exitAtLow) = strategy.ProcessPrice(candle.Low);
            if (exitAtLow is not null && open is not null)
            {
                trades[^1] = open with { ExitTimeUtc = candle.OpenTime, ExitPrice = exitAtLow.Price, ExitReason = exitAtLow.Reason };
                open = null;
            }
            var (highSignal, exitAtHigh) = strategy.ProcessPrice(candle.High);
            signal ??= highSignal;
            if (exitAtHigh is not null && open is not null)
            {
                trades[^1] = open with { ExitTimeUtc = candle.OpenTime, ExitPrice = exitAtHigh.Price, ExitReason = exitAtHigh.Reason };
                open = null;
            }
            if (signal is not null)
            {
                open = new BollingerSpotBacktestTrade(symbol, settings, signal.Setup.CandidateTimeUtc, candle.OpenTime,
                    signal.Price, signal.Stop, signal.Target, null, null, "");
                trades.Add(open);
            }
        }
        return new(trades);
    }
}
