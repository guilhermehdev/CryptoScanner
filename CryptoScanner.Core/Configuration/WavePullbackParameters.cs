namespace CryptoScanner.Core.Configuration;

/// <summary>
/// Parâmetros próprios da estratégia Onda Pullback.
///
/// Os valores padrão preservam exatamente a hipótese inicial da estratégia.
/// Mantê-los fora do algoritmo permite testar uma variável por vez e registrar
/// no diagnóstico qual configuração gerou cada backtest.
/// </summary>
public sealed record WavePullbackParameters
{
    public int EmaPeriod { get; init; } = 34;
    public int SlopeLookback { get; init; } = 5;
    public int RsiPeriod { get; init; } = 14;
    public int StructureLookback { get; init; } = 20;
    public decimal TouchTolerancePercent { get; init; } = 0.35m;
    public decimal LongMinimumSlopeDegrees { get; init; } = 15m;
    public decimal ShortMinimumSlopeDegrees { get; init; } = 15m;
    public decimal StrongSlopeDegrees { get; init; } = 45m;
    public decimal LongOverbought { get; init; } = 70m;
    public decimal ShortOversold { get; init; } = 30m;
    public decimal ConfirmationBodyRatio { get; init; } = 0.50m;
    public decimal BreakoutBufferAtr { get; init; } = 0m;
    public decimal MinimumStructuralRiskReward { get; init; } = 2m;
    public bool RequireTrendSideClose { get; init; }

    public static WavePullbackParameters Default { get; } = new();
}
