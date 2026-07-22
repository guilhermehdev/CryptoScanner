using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Backtest.Models;

public class BacktestTrade
{
    public DateTime EntryTime { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal ExitPrice { get; set; }

    public decimal ProfitPercent { get; set; }

    public bool Win { get; set; }
}