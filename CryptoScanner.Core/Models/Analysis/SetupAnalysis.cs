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
}