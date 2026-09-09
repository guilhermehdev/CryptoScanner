namespace CryptoScanner.Core.Models;

public sealed class LlmTradeOpinion
{
    public string Decisao { get; init; } = "AGUARDAR";
    public string Direcao { get; init; } = "NEUTRA";
    public int Confianca { get; init; }
    public string Tendencia { get; init; } = "INDEFINIDA";
    public decimal? Entrada { get; init; }
    public decimal? Stop { get; init; }
    public decimal? Tp1 { get; init; }
    public decimal? Tp2 { get; init; }
    public string[] Motivos { get; init; } = [];
    public string[] Riscos { get; init; } = [];
}
