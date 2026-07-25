namespace CryptoScanner.Core.Models;

public sealed class SignalSnapshot
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required decimal Score { get; init; }
    public required string Signal { get; init; }
    public required decimal PreviousScore { get; init; }
    public required decimal TakeProfit { get; init; }
    public required decimal StopLoss { get; init; }
    public required string Profile { get; init; }
    public required string MarketRegime { get; init; }

    // Indicadores brutos
    public decimal Rsi { get; init; }
    public decimal Adx { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal EmaDistanceAtr { get; init; }
    public decimal SwingUsageAtr { get; init; }
    public decimal VolumeSpike { get; init; }
    public decimal VolumeImbalance { get; init; }
    public decimal RelativeStrength { get; init; }
    public decimal RiskReward { get; init; }

    // Score Breakdown (sub-scores que compõem o OpportunityScore)
    public int TrendScore { get; init; }
    public int StructureScore { get; init; }
    public int VolumeScore { get; init; }
    public int CandleScore { get; init; }
    public int SetupScore { get; init; }
    public int MomentumScore { get; init; }
    public int VolatilityScore { get; init; }
    public int TrendStrengthScore { get; init; }

    // Contexto qualitativo
    public string PatternName { get; init; } = "";
    public string SmartMoneyLabel { get; init; } = "";
    public string BreakoutSource { get; init; } = "";
    public bool IsBullTrap { get; init; }
    public bool IsBearTrap { get; init; }
}