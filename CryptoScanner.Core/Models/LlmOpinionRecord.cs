namespace CryptoScanner.Core.Models;

public sealed class LlmOpinionRecord
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Symbol { get; set; } = "";
    public string Profile { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public decimal AnalysisPrice { get; set; }
    public string ScannerSignal { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Confidence { get; set; }
    public string Trend { get; set; } = "";
    public decimal? Entry { get; set; }
    public decimal? Stop { get; set; }
    public decimal? Tp1 { get; set; }
    public decimal? Tp2 { get; set; }
    public string Reasons { get; set; } = "";
    public string Risks { get; set; } = "";
    public int? SimulatedTradeId { get; set; }
    public bool OutcomeEvaluated { get; set; }
    public decimal? OutcomePercent { get; set; }
    public string OutcomeReason { get; set; } = "";
    public DateTime? OutcomeAt { get; set; }
}
