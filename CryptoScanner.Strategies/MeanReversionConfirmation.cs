using CryptoScanner.Core.Models;

namespace CryptoScanner.Strategies;

/// <summary>
/// Confirma a recuperação após uma reação de reversão à média.
/// A entrada só é aceita quando o candle posterior fecha acima da máxima da reação.
/// </summary>
public static class MeanReversionConfirmation
{
    public const decimal MinimumEmaDistanceAtr = 1.0m;
    public const decimal StopBufferAtr = 0.15m;

    public static bool IsConfirmed(
        Candle reactionCandle,
        Candle confirmationCandle,
        bool wasUptrend,
        decimal reactionClose,
        decimal reactionEma21,
        decimal reactionAtr,
        bool hasBullishReaction)
    {
        return wasUptrend &&
               reactionAtr > 0 &&
               reactionEma21 > reactionClose &&
               (reactionEma21 - reactionClose) / reactionAtr >= MinimumEmaDistanceAtr &&
               hasBullishReaction &&
               confirmationCandle.Close > reactionCandle.High;
    }

    public static decimal StopBelowReaction(Candle reactionCandle, decimal currentAtr) =>
        reactionCandle.Low - currentAtr * StopBufferAtr;
}
