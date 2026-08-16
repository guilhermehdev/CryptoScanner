namespace CryptoScanner.Core.Models.Analysis;

public sealed class TrendAnalysis
{
    public decimal Close { get; init; }
    public decimal Ema21 { get; init; }
    public decimal Ema50 { get; init; }
    public decimal Ema200 { get; init; }
    public decimal Rsi { get; init; }
    public decimal Atr { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal Adx { get; init; }
    public int Score { get; init; }
    public int MomentumScore { get; init; }
    public int VolatilityScore { get; init; }
    public int TrendStrengthScore { get; init; }
    public string Direction { get; init; } = "LATERAL";

    // Fase A do lado de venda — as 3 EMAs alinhadas (Preço < EMA21 < EMA50 < EMA200) E
    // caindo nos últimos candles, não só alinhadas num instante isolado. Long não usa isso.
    public bool IsBearishTrendConfirmed { get; init; }

    // RSI — confirmação, não portão obrigatório (diferente de Estrutura/EMA acima, que
    // bloqueiam elegibilidade). Momentum: topo do preço mais baixo E topo do RSI mais
    // baixo. Divergência: topo do preço MAIS ALTO mas topo do RSI mais baixo — sinal mais
    // forte dos dois. Nenhum dos dois ainda influencia o Score ou a elegibilidade — ficam
    // disponíveis como dado, aguardando integração (ver AssetAnalyzer.cs).
    public bool IsBearishMomentumConfirmed { get; init; }

    public bool IsBearishRsiDivergence { get; init; }

    // Diagnóstico — investigação de 12/2026 sobre Momentum/Divergência sempre virem false
    // no Bollinger Reversal (63/63 trades desmarcados). Indica se MarketStructureAnalyzer
    // conseguiu identificar 2 swing highs válidos (pré-requisito pra Momentum/Divergência
    // sequer serem calculados) — separa "dado indisponível" (estrutura sem 2 topos/fundos
    // na janela) de "dado disponível mas o sinal genuinamente não ocorreu".
    public bool HadSwingHighDataAvailable { get; init; }
}