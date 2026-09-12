using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models.Analysis;

public sealed class AssetAnalysis
{
    public TradeDirection Direction { get; init; } = TradeDirection.Long;
    public CryptoScanner.Core.Configuration.EntryStrategy EntryStrategy { get; init; }
    public required string Symbol { get; init; }
    public required TrendAnalysis Trend { get; init; }
    public required VolumeAnalysis Volume { get; init; }
    public required StructureAnalysis Structure { get; init; }
    public required RiskAnalysis Risk { get; init; }
    public required CandleAnalysis Candle { get; init; }
    public required SetupAnalysis Setup { get; init; }
    public decimal OpportunityScore { get; set; }
    public decimal PreviousScore { get; set; }
    public decimal ScoreVariation { get; set; }
    public decimal RetailFlowScore { get; set; } = 50m;
    public BuyingPressureResult BuyingPressure { get; set; } = BuyingPressureResult.Unavailable("aguardando atualização.");

    public string Signal => Direction == TradeDirection.Short
        ? (OpportunityScore >= 70 ? "VENDA+" : OpportunityScore >= 55 ? "VENDA" : OpportunityScore >= 40 ? "MONITORAR" : "IGNORAR")
        : OpportunityScore >= 70 ? "COMPRA+" :
                            OpportunityScore >= 55 ? "COMPRA" :
                            OpportunityScore >= 40 ? "MONITORAR" : "IGNORAR";

    public bool IsEliteSetup =>
        OpportunityScore >= 75 &&
        Trend.Direction == (Direction == TradeDirection.Long ? "ALTA" : "BAIXA") &&
        Risk.RiskReward >= 2.5m &&
        Candle.RejectionScore <= 0.40m;
}
