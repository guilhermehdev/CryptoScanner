namespace CryptoScanner.Core.Models;

public enum BollingerSpotVariant
{
    Disabled,
    Bollinger20x3,
    Bollinger20x23,
    Bollinger25x3,
    Bollinger25x23,
    // Mantidos para conseguir ler simulações históricas, mas não são perfis ativos.
    Bollinger25x28,
    Bollinger25x32,
    Bollinger25x29,
    Bollinger20x32
}

public enum BollingerSpotTimeframe
{
    FourHours,
    // Mantido para restaurar simulações históricas M3/M4; não é perfil ativo.
    SixHours,
    TwelveHours,
    Daily
}
public enum BollingerSpotSetupDirection { Buy, Sell }

public sealed record BollingerSpotSettings(BollingerSpotVariant Variant, BollingerSpotTimeframe Timeframe)
{
    public int Period => Variant switch
    {
        BollingerSpotVariant.Bollinger25x3 or BollingerSpotVariant.Bollinger25x23 or
        BollingerSpotVariant.Bollinger25x28 or BollingerSpotVariant.Bollinger25x32 or BollingerSpotVariant.Bollinger25x29 => 25,
        _ => 20
    };
    public decimal StandardDeviations => Variant switch
    {
        BollingerSpotVariant.Bollinger20x23 or BollingerSpotVariant.Bollinger25x23 => 2.3m,
        BollingerSpotVariant.Bollinger25x28 => 2.8m,
        BollingerSpotVariant.Bollinger25x29 => 2.9m,
        BollingerSpotVariant.Bollinger20x32 or BollingerSpotVariant.Bollinger25x32 => 3.2m,
        _ => 3m
    };
    public string Interval => Timeframe switch
    {
        BollingerSpotTimeframe.FourHours => "4h",
        BollingerSpotTimeframe.SixHours => "6h",
        BollingerSpotTimeframe.TwelveHours => "12h",
        _ => "1d"
    };
    public TimeSpan CandleDuration => Timeframe switch
    {
        BollingerSpotTimeframe.FourHours => TimeSpan.FromHours(4),
        BollingerSpotTimeframe.SixHours => TimeSpan.FromHours(6),
        BollingerSpotTimeframe.TwelveHours => TimeSpan.FromHours(12),
        _ => TimeSpan.FromDays(1)
    };
    public string DisplayName => $"Bollinger {Period}/{StandardDeviations:0.#} · {Timeframe switch { BollingerSpotTimeframe.FourHours => "4 horas", BollingerSpotTimeframe.SixHours => "6 horas", BollingerSpotTimeframe.TwelveHours => "12 horas", _ => "Diário" }}";
    public static BollingerSpotSettings Default { get; } = new(BollingerSpotVariant.Bollinger25x3, BollingerSpotTimeframe.Daily);
}

/// <summary>Hipóteses pré-definidas para evitar uma busca livre de parâmetros.</summary>
public sealed record BollingerSpotExperimentProfile(string Key, string DisplayName, BollingerSpotSettings Settings)
{
    public static IReadOnlyList<BollingerSpotExperimentProfile> All { get; } =
    [
        new("Base", "Base — Bollinger 25/3 · Diário", new(BollingerSpotVariant.Bollinger25x3, BollingerSpotTimeframe.Daily)),
        new("A", "Teste A — Bollinger 20/3,2 · Diário", new(BollingerSpotVariant.Bollinger20x32, BollingerSpotTimeframe.Daily)),
        new("B", "Teste B — Bollinger 25/3,2 · Diário", new(BollingerSpotVariant.Bollinger25x32, BollingerSpotTimeframe.Daily))
    ];

    public static BollingerSpotExperimentProfile FromKey(string? key) =>
        All.FirstOrDefault(profile => string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static BollingerSpotExperimentProfile FromSettings(BollingerSpotSettings settings) =>
        All.FirstOrDefault(profile => profile.Settings == settings) ?? All[0];
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
