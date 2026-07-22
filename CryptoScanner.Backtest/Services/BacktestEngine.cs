using CryptoScanner.Backtest.Models;
using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Backtest.Services;

public class BacktestEngine
{
    public BacktestResult Run(
        List<Candle> candles)
    {
        BacktestResult result = new();

        for (int i = 200; i < candles.Count - 24; i++)
        {
            decimal entry =
                candles[i].Close;

            decimal exit =
                candles[i + 24].Close;

            decimal profit =
                ((exit - entry) / entry) * 100;

            bool win = profit > 0;

            result.Trades++;

            if (win)
                result.Wins++;
            else
                result.Losses++;

            result.NetProfit += profit;

            result.TradesList.Add(
                new BacktestTrade
                {
                    EntryTime =
                        candles[i].OpenTime,

                    EntryPrice =
                        entry,

                    ExitPrice =
                        exit,

                    ProfitPercent =
                        profit,

                    Win =
                        win
                });
        }

        result.WinRate =
            (decimal)result.Wins /
            result.Trades * 100;

        return result;
    }
}

