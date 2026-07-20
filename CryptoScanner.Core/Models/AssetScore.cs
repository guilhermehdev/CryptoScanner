using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models;

public class AssetScore
{
    public string Symbol { get; set; } = "";

    public int Score { get; set; }

    public decimal Close { get; set; }

    public decimal Ema21 { get; set; }

    public decimal Ema50 { get; set; }

    public decimal Ema200 { get; set; }
}