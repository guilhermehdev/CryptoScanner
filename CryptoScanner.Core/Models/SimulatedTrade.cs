namespace CryptoScanner.Core.Models;

public sealed class SimulatedTrade
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal StopLoss { get; set; }
    public string Note { get; set; } = "";
    public string Profile { get; set; } = "";

    // Raio-x completo do momento da entrada — mesmo nível de detalhe do SignalSnapshot,
    // pra permitir análise futura (ex.: "meus trades com Score>75 tiveram Win Rate melhor?").
    public decimal ScoreAtEntry { get; set; }
    public decimal Rsi { get; set; }
    public decimal Adx { get; set; }
    public decimal AtrPercent { get; set; }
    public decimal EmaDistanceAtr { get; set; }
    public decimal SwingUsageAtr { get; set; }
    public decimal VolumeSpike { get; set; }
    public decimal VolumeImbalance { get; set; }
    public decimal RelativeStrength { get; set; }
    public decimal RiskRewardAtEntry { get; set; }
    public decimal TrendScore { get; set; }
    public decimal StructureScore { get; set; }
    public decimal VolumeScore { get; set; }
    public decimal CandleScore { get; set; }
    public decimal SetupScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal VolatilityScore { get; set; }
    public decimal TrendStrengthScore { get; set; }
    public string PatternName { get; set; } = "";
    public string SmartMoneyLabel { get; set; } = "";
    public string BreakoutSource { get; set; } = "";
    public string MarketRegime { get; set; } = "";
    public bool IsBullTrap { get; set; }
    public bool IsBearTrap { get; set; }

    public bool Closed { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? OutcomePercent { get; set; }
    public string ExitReason { get; set; } = ""; // "TP" | "SL" | "Manual"

    public bool IsOpen => !Closed;

    // Preenchidos em memória na hora de carregar a tela — não são persistidos no banco,
    // já que representam o preço "agora", não um dado histórico do trade.
    public decimal? CurrentPrice { get; set; }
    public decimal? UnrealizedPnLPercent { get; set; }
}