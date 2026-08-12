namespace CryptoScanner.Core.Configuration;

public sealed class ScanProfile
{
    public required string Name { get; init; }
    public required string CandleInterval { get; init; }
    public required int EvaluationHours { get; init; }
    public required int DefensiveBreakoutLookback { get; init; }
    public required int DuplicateSignalWindowDays { get; init; }

    public static readonly ScanProfile Scalp = new()
    {
        Name = "Scalp",
        CandleInterval = "15m",
        // 24 candles de paciência, mesma proporção usada no Intraday (24h em candles de
        // 1h = 24 candles). Em candles de 15min, 24 candles = 6h. Ainda não validado por
        // Backtest — valor de partida, sujeito a ajuste no ciclo de calibração.
        EvaluationHours = 6,
        DefensiveBreakoutLookback = 8,
        // Menor unidade representável (dias inteiros) — o ideal proporcional seria uma
        // fração de dia (~0,25, batendo com 6h de timeout), mas o campo é int. Mesmo
        // valor mínimo já usado no Intraday.
        DuplicateSignalWindowDays = 1
    };

    public static readonly ScanProfile Intraday = new()
    {
        Name = "Intraday",
        CandleInterval = "1h",
        EvaluationHours = 24,
        DefensiveBreakoutLookback = 8,
        DuplicateSignalWindowDays = 1
    };

    public static readonly ScanProfile Swing = new()
    {
        Name = "Swing",
        CandleInterval = "4h",
        EvaluationHours = 240,
        DefensiveBreakoutLookback = 8,
        DuplicateSignalWindowDays = 7
    };
}