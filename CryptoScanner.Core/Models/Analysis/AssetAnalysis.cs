namespace CryptoScanner.Core.Models.Analysis;

public sealed class AssetAnalysis
{
    public required string Symbol { get; init; }
    public required TrendAnalysis Trend { get; init; }
    public required VolumeAnalysis Volume { get; init; }
    public required StructureAnalysis Structure { get; init; }
    public required RiskAnalysis Risk { get; init; }
    public required CandleAnalysis Candle { get; init; }
    public required SetupAnalysis Setup { get; init; }
    public decimal OpportunityScore { get; set; }

    public string Signal => OpportunityScore >= 70 ? "STRONG BUY" :
                            OpportunityScore >= 55 ? "BUY" :
                            OpportunityScore >= 40 ? "WATCH" : "IGNORE";

    public bool IsEliteSetup =>
        OpportunityScore >= 75 &&
        Trend.Direction == "ALTA" &&
        Risk.RiskReward >= 2.5m &&
        Candle.RejectionScore <= 0.40m;
}
