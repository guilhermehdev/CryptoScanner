namespace CryptoScanner.Core.Models;

public sealed record FlowCandle(long OpenTime, decimal Open, decimal High, decimal Low,
    decimal Close, decimal Volume, decimal BuyVolume);

public sealed record OpenInterestSample(long Timestamp, decimal Value);

public sealed record BuyingPressureResult(decimal? Score, string Details)
{
    public BuyingPressureMeasurements? Measurements { get; init; }
    public static BuyingPressureResult Unavailable(string reason) => new(null, $"Sem dados: {reason}");
}

public sealed record BuyingPressureMeasurements(long WindowEndMs, decimal ReferencePrice,
    decimal BuyRatio, decimal Persistence, decimal PriceChangePercent, decimal RelativeVolume,
    decimal OpenInterestChangePercent, decimal ExtensionPenalty, decimal BaselinePrice, decimal Atr);

public sealed record BuyingPressureSnapshot(string Symbol, long WindowEndMs, long CollectedAtMs,
    BuyingPressureResult Result, MarketFlowData RawData)
{
    public const string FormulaVersion = "buying-pressure-v1";
}

public sealed record PressurePriceTarget(string Symbol, long CloseTimeMs);
public sealed record PressurePrice(string Symbol, long CloseTimeMs, decimal Price, long CollectedAtMs, bool Reconstructed);
