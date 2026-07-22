using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Backtest.Models;

public class BacktestResult
{
    public int Trades { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public decimal WinRate { get; set; }

    public decimal NetProfit { get; set; }

    public List<BacktestTrade> TradesList { get; set; } = [];
}
