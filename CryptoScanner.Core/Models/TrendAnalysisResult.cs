using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models;

public class TrendAnalysisResult
{
    public decimal Close { get; set; }

    public decimal Ema21 { get; set; }

    public decimal Ema50 { get; set; }

    public decimal Ema200 { get; set; }

    public decimal Rsi { get; set; }

    public decimal Atr { get; set; }

    public decimal AtrPercent { get; set; }

    public decimal Adx { get; set; }

    public int TrendScore { get; set; }

    public int MomentumScore { get; set; }

    public int VolatilityScore { get; set; }

    public int TrendStrengthScore { get; set; }

    public string TrendDirection { get; set; } = "LATERAL";
}
