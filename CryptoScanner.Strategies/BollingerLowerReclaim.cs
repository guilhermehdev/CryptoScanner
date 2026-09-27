using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

/// <summary>
/// Confirma uma reversão Long na banda inferior: primeiro há o toque e, no candle
/// seguinte, o fechamento precisa recuperar a máxima daquele candle.
/// </summary>
public static class BollingerLowerReclaim
{
    public const decimal StopBufferAtr = 0.25m;

    public static bool IsConfirmed(
        Candle touchCandle,
        Candle confirmationCandle,
        decimal? touchLowerBand,
        decimal? currentLowerBand,
        decimal? currentMiddleBand)
    {
        return touchLowerBand is > 0 &&
               currentLowerBand is > 0 &&
               currentMiddleBand is > 0 &&
               touchCandle.Low <= touchLowerBand.Value &&
               confirmationCandle.Close > touchCandle.High &&
               confirmationCandle.Close > currentLowerBand.Value &&
               confirmationCandle.Close < currentMiddleBand.Value;
    }

    public static decimal StopBelowLowerBand(decimal lowerBand, decimal atr) =>
        lowerBand - atr * StopBufferAtr;
}
