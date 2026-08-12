namespace CryptoScanner.Core.Models.Analysis;

public sealed class StructureAnalysis
{
    public int Score { get; init; }
    public bool IsUptrend { get; init; }
    public bool IsDowntrend { get; init; }
    public bool IsStrongUptrend { get; init; }
    public bool IsStrongDowntrend { get; init; }
    public bool HasBreakOfStructure { get; init; }
    public bool HasChangeOfCharacter { get; init; }

    // Espelhos pro lado de baixa (Fase A do lado de venda) — ver MarketStructureAnalyzer.cs.
    public bool HasBearishBreakOfStructure { get; init; }
    public bool HasBearishChangeOfCharacter { get; init; }

    // Topos/fundos brutos (sem exigir rompimento junto) — usados pra RSI momentum/divergência,
    // que compara a sequência de topos do preço com a sequência de topos do RSI, independente
    // de já ter havido rompimento ou não.
    public bool HasHigherHigh { get; init; }
    public bool HasLowerHigh { get; init; }

    // Posição dos dois últimos topos — usada pra cruzar com a série de RSI no mesmo ponto
    // exato (ver AssetAnalyzer.AnalyzeTrend). -1 = não disponível.
    public int LastSwingHighIndex { get; init; } = -1;
    public int PrevSwingHighIndex { get; init; } = -1;

    public bool LiquiditySweepHigh { get; init; }
    public bool LiquiditySweepLow { get; init; }
    public bool IsBullTrap { get; init; }
    public bool IsBearTrap { get; init; }
    public string SmartMoneyLabel { get; init; } = "";
}