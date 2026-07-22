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

    public decimal Rsi { get; set; }

    public decimal RelativeVolume { get; set; }

    public decimal Atr { get; set; }

    public decimal AtrPercent { get; set; }

    public int Score1H { get; set; }

    public int Score4H { get; set; }

    public int Score1D { get; set; }

    public decimal FinalScore { get; set; }

    public bool IsBreakout { get; set; }

    public decimal Resistance { get; set; }

    public string CloseFormatted =>
    Close >= 1
        ? Close.ToString("N2")
        : Close.ToString("N8");

    public string Signal
    {
        get
        {
            if (FinalScore >= 70)
                return "STRONG BUY";

            if (FinalScore >= 55)
                return "BUY";

            if (FinalScore >= 40)
                return "WATCH";

            return "IGNORE";
        }
    }

    public int MarketStructureScore { get; set; }
    public int MomentumScore { get; set; }
    public int VolumeScore { get; set; }
    public int VolatilityScore { get; set; }
    public decimal Adx { get; set; }
    public decimal VolumeSpike { get; set; }
    public int TrendStrengthScore { get; set; }
    public bool IsConsolidating { get; set; }
    public string TrendDirection { get; set; } = "";
}