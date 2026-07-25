using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models
{
    public class SignalHistory
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; } = "";
        public decimal Price { get; set; }
        public decimal FinalScore { get; set; }
        public string Signal { get; set; } = "";
        public decimal? OutcomePrice { get; set; }
        public decimal? OutcomePercent { get; set; }
        public bool Evaluated { get; set; }
        public decimal OpportunityScore { get; set; }
        public decimal? PreviousScore { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal StopLoss { get; set; }
        public string ExitReason { get; set; } = "";
        public string Profile { get; set; } = "";
        public string MarketRegime { get; set; } = "";

        public decimal Rsi { get; set; }
        public decimal Adx { get; set; }
        public decimal AtrPercent { get; set; }
        public decimal EmaDistanceAtr { get; set; }
        public decimal SwingUsageAtr { get; set; }
        public decimal VolumeSpike { get; set; }
        public decimal VolumeImbalance { get; set; }
        public decimal RelativeStrength { get; set; }
        public decimal RiskReward { get; set; }

        public int TrendScore { get; set; }
        public int StructureScore { get; set; }
        public int VolumeScore { get; set; }
        public int CandleScore { get; set; }
        public int SetupScore { get; set; }
        public int MomentumScore { get; set; }
        public int VolatilityScore { get; set; }
        public int TrendStrengthScore { get; set; }

        public string PatternName { get; set; } = "";
        public string SmartMoneyLabel { get; set; } = "";
        public string BreakoutSource { get; set; } = "";
        public bool IsBullTrap { get; set; }
        public bool IsBearTrap { get; set; }
    }
}