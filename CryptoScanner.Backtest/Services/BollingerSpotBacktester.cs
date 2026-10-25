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
            // Primeiro percorremos a faixa do candle ATUAL contra setups criados por candles
            // ANTERIORES. Só depois confirmamos o fechamento atual como possível candidato.
            // A ordem inversa invalidava todo setup de compra na própria mínima do candle que
            // o criou, produzindo falsamente zero operações.
            if (open is not null && candle.Low <= open.Stop)
            {
                var (_, exit) = strategy.ProcessPrice(open.Stop);
                if (exit is not null)
                {
                    trades[^1] = open with { ExitTimeUtc = candle.OpenTime, ExitPrice = open.Stop, ExitReason = exit.Reason };
                    open = null;
                }
            }

            BollingerSpotSignal? signal = null;
            void Apply((BollingerSpotSignal? Signal, BollingerSpotExit? Exit) result)
            {
                signal ??= result.Signal;
                if (result.Exit is not null && open is not null)
                {
                    trades[^1] = open with
                    {
                        ExitTimeUtc = candle.OpenTime,
                        ExitPrice = result.Exit.Price,
                        ExitReason = result.Exit.Reason
                    };
                    open = null;
                }
            }

            // Sem intrabar disponível, assumimos o caminho conservador. Stop tem prioridade
            // para LONG. Num setup de venda, testamos primeiro a máxima para não permitir que
            // uma saída aconteça se a invalidação acima da máxima também ocorreu na barra.
            if (open is null || candle.Low > open.Stop)
            {
                if (strategy.PendingSetup?.Direction == BollingerSpotSetupDirection.Sell)
                {
                    Apply(strategy.ProcessPrice(candle.High));
                    Apply(strategy.ProcessPrice(candle.Low));
                }
                else
                {
                    Apply(strategy.ProcessPrice(candle.Low));
                    Apply(strategy.ProcessPrice(candle.High));
                }
            }

            if (signal is not null)
            {
                open = new BollingerSpotBacktestTrade(symbol, settings, signal.Setup.CandidateTimeUtc, candle.OpenTime,
                    signal.Price, signal.Stop, signal.Target, null, null, "");
                trades.Add(open);
            }

            // O candle só vira setup depois que toda a sua faixa já foi processada. Portanto,
            // uma compra nunca ocorre no mesmo candle que fechou abaixo da banda.
            closed.Add(candle);
            strategy.ProcessClosedCandle(closed);
        }
        return new(trades);
    }
}
