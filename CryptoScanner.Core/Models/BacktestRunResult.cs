namespace CryptoScanner.Core.Models;

public sealed class BacktestRunResult
{
    public int Id { get; set; }
    public string SignatureHash { get; set; } = "";
    public DateTime SavedAt { get; set; }
    public string Label { get; set; } = "";
    public string Profile { get; set; } = "";
    public string RiskMode { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Symbols { get; set; } = "";
    public int SymbolCount { get; set; }
    public decimal MinScore { get; set; }
    public decimal MinResistanceDistanceSwing { get; set; }
    public decimal MinResistanceDistanceAtr { get; set; }
    public decimal MinVolumeSpike { get; set; }
    public decimal MinRiskReward { get; set; }
    public decimal MinStopDistancePercent { get; set; }
    public decimal MaxRiskReward { get; set; }
    public bool EnablePullbackBounce { get; set; }
    public int? EvaluationHoursOverride { get; set; }
    public bool EnableBollingerScoring { get; set; }
    public int TotalTrades { get; set; }
    public double WinRate { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AvgRiskRewardAtEntry { get; set; }
    public double BreakEvenWinRate { get; set; }
    public double Edge { get; set; }
    public bool EnableVolatilityScoringPhaseB { get; set; }
}