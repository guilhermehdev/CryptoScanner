namespace CryptoScanner.Core.Models.Analysis;

public sealed class SetupAnalysis
{
    public int Score { get; init; }
    public bool IsBreakout { get; init; }
    public bool IsShortTermBreakout { get; init; }
    public decimal RelativeStrength { get; init; }
    public bool IsConsolidating { get; init; }
    public bool IsOverextended { get; init; }
    public decimal EmaDistanceAtr { get; init; }
    public decimal SwingUsageAtr { get; init; }

    // Caminho A — repique dentro de tendência de alta já estabelecida.
    public bool IsPullbackBounce { get; init; }

    // Reversão à média (Scalp) — preço esticado abaixo da EMA21 dentro de tendência de
    // alta, com sinal de virada no candle atual. Alvo é a volta pra EMA21, não resistência.
    public bool IsMeanReversionSetup { get; init; }

    // Reversão de Bollinger (Fase A do lado de venda) — banda superior + resistência como
    // zona de gatilho, com rejeição confirmada e filtro contra "andar na banda" (momentum
    // de alta forte demais pra brigar). Ver AssetAnalyzer.cs pros detalhes de cada condição.
    public bool IsBollingerReversalSetup { get; init; }

    // Caminho de RSI baixo (Fase 3 do roadmap, 16/08/2026) — descoberto via análise por
    // fator numa amostra de 3.885 trades (limiares soltos, pra exploração): RSI<45 na
    // entrada teve Win Rate 64-79% nos trades que saíram por TIMEOUT (medida mais limpa,
    // não depende de bater TP/SL num preço exato), contra 32-45% pra RSI≥55 — checado
    // também que o lado "RSI alto prejudica" é robusto a outlier (retorno total negativo
    // mesmo tirando os 5 melhores trades da faixa). Caminho ADICIONAL (OR) — não substitui
    // nenhum dos outros, só abre mais uma porta de entrada pra candidatos com RSI favorável
    // que não bateriam rompimento clássico. Long apenas, atrás de EnableLowRsiPath
    // (desligado por padrão).
    public bool IsLowRsiSetup { get; init; }
}