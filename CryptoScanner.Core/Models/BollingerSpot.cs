namespace CryptoScanner.Core.Models;

public enum BollingerSpotVariant
{
    Disabled,
    Bollinger20x3,
    Bollinger20x23,
    Bollinger25x3,
    Bollinger25x23
}

public enum BollingerSpotTimeframe { FourHours, Daily }
public enum BollingerSpotSetupDirection { Buy, Sell }

public sealed record BollingerSpotSettings(BollingerSpotVariant Variant, BollingerSpotTimeframe Timeframe)
{
    public int Period => Variant switch
    {
        BollingerSpotVariant.Bollinger25x3 or BollingerSpotVariant.Bollinger25x23 => 25,
        _ => 20
    };
    public decimal StandardDeviations => Variant switch
    {
        BollingerSpotVariant.Bollinger20x23 or BollingerSpotVariant.Bollinger25x23 => 2.3m,
        _ => 3m
    };
    public string Interval => Timeframe == BollingerSpotTimeframe.FourHours ? "4h" : "1d";
    public string DisplayName => $"Bollinger {Period}/{StandardDeviations:0.#} · {(Timeframe == BollingerSpotTimeframe.FourHours ? "4 horas" : "Diário")}";
    public static BollingerSpotSettings Default { get; } = new(BollingerSpotVariant.Bollinger20x3, BollingerSpotTimeframe.Daily);
}

public sealed record BollingerSpotSetup(string Symbol, BollingerSpotSettings Settings, DateTime CandidateTimeUtc,
    decimal High, decimal Low, decimal Middle, decimal Upper, decimal Lower, BollingerSpotSetupDirection Direction);

public sealed record BollingerSpotSignal(string Symbol, BollingerSpotSetup Setup, decimal Price, decimal Stop, decimal Target,
    string Reason);

public sealed class BollingerSpotPosition
{
    public required string Symbol { get; init; }
    public required BollingerSpotSettings Settings { get; init; }
    public required DateTime SetupTimeUtc { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal Stop { get; init; }
    public required decimal Target { get; init; }
}

public sealed record BollingerSpotExit(string Symbol, decimal Price, string Reason);
