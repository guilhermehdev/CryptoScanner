namespace CryptoScanner.Core.Configuration;

public sealed class ScanProfile
{
    public required string Name { get; init; }
    public required string CandleInterval { get; init; }
    public required int EvaluationHours { get; init; }
    public required int DefensiveBreakoutLookback { get; init; }
    public required int DuplicateSignalWindowDays { get; init; }

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