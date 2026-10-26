using CryptoScanner.Core.Models;
using CryptoScanner.Indicators.Indicators;

namespace CryptoScanner.Strategies;

/// <summary>
/// Estratégia experimental Spot. O candle candidato só cria um setup; a entrada ou a
/// saída depende de preço posterior, nunca do candle que fechou fora da banda.
/// </summary>
public sealed class BollingerSpotStrategy
{
    private readonly string _symbol;
    private readonly BollingerSpotSettings _settings;
    private readonly HashSet<DateTime> _processedCandidates = [];

    public BollingerSpotSetup? PendingSetup { get; private set; }
    public BollingerSpotPosition? Position { get; private set; }

    public void RestorePosition(BollingerSpotPosition position)
    {
        if (!string.Equals(position.Symbol, _symbol, StringComparison.OrdinalIgnoreCase) || position.Settings != _settings)
            throw new ArgumentException("A posição restaurada não pertence a esta estratégia.");
        Position = position;
    }

    public BollingerSpotStrategy(string symbol, BollingerSpotSettings settings)
    {
        _symbol = symbol.ToUpperInvariant();
        _settings = settings;
    }

    public BollingerSpotSetup? ProcessClosedCandle(IReadOnlyList<Candle> closedCandles)
    {
        if (_settings.Variant == BollingerSpotVariant.Disabled || closedCandles.Count < _settings.Period)
            return null;

        var candles = closedCandles.ToList();
        var bands = BollingerBandsIndicator.Calculate(candles, _settings.Period, _settings.StandardDeviations);
        int index = candles.Count - 1;
        if (bands.Middle[index] is not decimal middle || bands.Upper[index] is not decimal upper || bands.Lower[index] is not decimal lower)
            return null;

        var candle = candles[index];
        if (!_processedCandidates.Add(candle.OpenTime)) return null;

        BollingerSpotSetupDirection? direction = candle.Close < lower
            ? BollingerSpotSetupDirection.Buy
            : candle.Close > upper ? BollingerSpotSetupDirection.Sell : null;
        if (direction is null || (direction == BollingerSpotSetupDirection.Sell && Position is null))
            return null;

        PendingSetup = new BollingerSpotSetup(_symbol, _settings, candle.OpenTime, candle.High, candle.Low,
            middle, upper, lower, direction.Value);
        return PendingSetup;
    }

    public (BollingerSpotSignal? Signal, BollingerSpotExit? Exit) ProcessPrice(decimal price)
    {
        if (price <= 0) return (null, null);

        if (Position is not null)
        {
            if (price <= Position.Stop)
                return Close(price, "Stop estrutural atingido");
            if (price >= Position.Target)
                return Close(price, "Alvo na média central atingido");
        }

        var setup = PendingSetup;
        if (setup is null) return (null, null);

        if (setup.Direction == BollingerSpotSetupDirection.Buy)
        {
            if (price <= setup.Low)
            {
                PendingSetup = null;
                return (null, null);
            }
            if (price > setup.High && Position is null)
            {
                PendingSetup = null;
                Position = new BollingerSpotPosition
                {
                    Symbol = _symbol, Settings = _settings, SetupTimeUtc = setup.CandidateTimeUtc,
                    EntryPrice = price, Stop = setup.Low, Target = setup.Middle
                };
                return (new BollingerSpotSignal(_symbol, setup, price, setup.Low, setup.Middle,
                    "Rompimento da máxima do candle fechado abaixo da banda inferior"), null);
            }
        }
        else
        {
            if (price >= setup.High)
            {
                PendingSetup = null;
                return (null, null);
            }
            if (price < setup.Low && Position is not null)
                return Close(price, "Perda da mínima do candle fechado acima da banda superior");
        }

        return (null, null);
    }

    private (BollingerSpotSignal?, BollingerSpotExit) Close(decimal price, string reason)
    {
        var exit = new BollingerSpotExit(_symbol, price, reason);
        Position = null;
        PendingSetup = null;
        return (null, exit);
    }
}

public static class BollingerSpotCandleAggregator
{
    public static List<Candle> AggregateUtc(IEnumerable<Candle> source, BollingerSpotTimeframe timeframe)
    {
        TimeSpan bucket = timeframe switch
        {
            BollingerSpotTimeframe.FourHours => TimeSpan.FromHours(4),
            BollingerSpotTimeframe.SixHours => TimeSpan.FromHours(6),
            BollingerSpotTimeframe.TwelveHours => TimeSpan.FromHours(12),
            _ => TimeSpan.FromDays(1)
        };
        return source.OrderBy(c => c.OpenTime).GroupBy(c => Bucket(c.OpenTime, bucket)).Select(group =>
        {
            var items = group.OrderBy(c => c.OpenTime).ToList();
            return new Candle { OpenTime = group.Key, Open = items[0].Open, High = items.Max(c => c.High),
                Low = items.Min(c => c.Low), Close = items[^1].Close, Volume = items.Sum(c => c.Volume) };
        }).ToList();
    }

    private static DateTime Bucket(DateTime value, TimeSpan bucket)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        long ticks = utc.Ticks - DateTime.UnixEpoch.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks + ticks / bucket.Ticks * bucket.Ticks, DateTimeKind.Utc);
    }
}
