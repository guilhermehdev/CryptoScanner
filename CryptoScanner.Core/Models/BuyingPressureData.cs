namespace CryptoScanner.Core.Models;

public sealed record FlowCandle(long OpenTime, decimal Open, decimal High, decimal Low,
    decimal Close, decimal Volume, decimal BuyVolume);

public sealed record OpenInterestSample(long Timestamp, decimal Value);

public sealed record BuyingPressureResult(decimal? Score, string Details)
{
    public static BuyingPressureResult Unavailable(string reason) => new(null, $"Sem dados: {reason}");
}
