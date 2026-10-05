using CryptoScanner.Core.Configuration;

namespace CryptoScanner.Core.Models;

/// <summary>
/// Registro observacional de uma situação técnica. Não representa uma ordem, sinal
/// elegível ou trade simulado.
/// </summary>
public sealed class TechnicalSetupAlert
{
    public int Id { get; init; }
    public required DateTime CandleOpenUtc { get; init; }
    public required DateTime EntryUtc { get; init; }
    public required DateTime RecordedUtc { get; init; }
    public required string Symbol { get; init; }
    public required TradeDirection Direction { get; init; }
    public required string Setup { get; init; }
    public required decimal Price { get; init; }
    public required decimal Score { get; init; }
    public required string Profile { get; init; }
    public required string MarketRegime { get; init; }
    public decimal? ReturnAfter1HourPercent { get; init; }
    public decimal? ReturnAfter6HoursPercent { get; init; }
    public decimal? ReturnAfter24HoursPercent { get; init; }
    public decimal? MaximumFavorable24HoursPercent { get; init; }
    public decimal? MaximumAdverse24HoursPercent { get; init; }
    public bool IsEvaluated => ReturnAfter24HoursPercent.HasValue;
    public DateTime CandleLocal => CandleOpenUtc.ToLocalTime();
}
