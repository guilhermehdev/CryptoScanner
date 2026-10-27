using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

/// <summary>
/// Implementação determinística da estratégia da onda usada pelo backtest.
///
/// A inclinação não é tratada como um horário literal. O cálculo usa o sentido
/// do ponteiro: delta positivo aponta para a faixa de alta e delta negativo para
/// a faixa de baixa. Os graus medem apenas a força dessa inclinação.
/// </summary>
public static class WavePullbackBacktestStrategy
{
    private const decimal DefaultMinimumRiskReward = 2m;

    public static WavePullbackSignal? Evaluate(
        IReadOnlyList<Candle> candles,
        TradeDirection direction,
        decimal minimumRiskReward = DefaultMinimumRiskReward,
        WavePullbackParameters? parameters = null)
    {
        parameters ??= WavePullbackParameters.Default;

        int emaPeriod = Math.Max(2, parameters.EmaPeriod);
        int slopeLookback = Math.Max(1, parameters.SlopeLookback);
        int rsiPeriod = Math.Max(2, parameters.RsiPeriod);
        int structureLookback = Math.Max(2, parameters.StructureLookback);
        decimal touchTolerancePercent = Math.Max(0m, parameters.TouchTolerancePercent);
        decimal minimumSlopeDegrees = Math.Max(0m, direction == TradeDirection.Long
            ? parameters.LongMinimumSlopeDegrees
            : parameters.ShortMinimumSlopeDegrees);
        decimal strongSlopeDegrees = Math.Max(minimumSlopeDegrees, parameters.StrongSlopeDegrees);
        decimal overbought = Math.Clamp(parameters.LongOverbought, 0m, 100m);
        decimal oversold = Math.Clamp(parameters.ShortOversold, 0m, 100m);
        decimal minimumConfirmationBodyRatio = Math.Clamp(parameters.ConfirmationBodyRatio, 0m, 1m);
        decimal breakoutBufferAtr = Math.Max(0m, parameters.BreakoutBufferAtr);
        decimal minimumStructuralRiskReward = Math.Max(
            minimumRiskReward,
            Math.Max(DefaultMinimumRiskReward, parameters.MinimumStructuralRiskReward));

        int minimumCandles = emaPeriod + slopeLookback + 2;
        if (candles.Count < minimumCandles)
            return null;

        minimumRiskReward = Math.Max(DefaultMinimumRiskReward, minimumRiskReward);

        int lastIndex = candles.Count - 1;
        Candle previous = candles[lastIndex - 1];
        Candle confirmation = candles[lastIndex];

        decimal emaNow = CalculateEma(candles, lastIndex, emaPeriod);
        decimal emaPast = CalculateEma(candles, lastIndex - slopeLookback, emaPeriod);
        decimal averageRange = CalculateAverageRange(candles, lastIndex, slopeLookback);
        decimal atr = CalculateAtr(candles, lastIndex, slopeLookback);
        if (averageRange <= 0)
            return null;

        decimal priceDelta = emaNow - emaPast;
        decimal slopeDegrees = (decimal)(Math.Atan2(
            Math.Abs((double)priceDelta),
            (double)(averageRange * slopeLookback)) * 180d / Math.PI);

        decimal tolerance = touchTolerancePercent / 100m;
        decimal bandUpper = emaPast + averageRange;
        decimal bandLower = emaPast - averageRange;
        bool touchedBand = previous.Low <= bandUpper * (1m + tolerance) &&
                           previous.High >= bandLower * (1m - tolerance);
        if (!touchedBand)
            return null;

        decimal confirmationRange = Math.Max(previous.High - previous.Low, confirmation.High - confirmation.Low);
        if (confirmationRange <= 0)
            return null;

        decimal confirmationBodyRatio = Math.Abs(confirmation.Close - confirmation.Open) / confirmationRange;
        bool bullishConfirmation = confirmation.Close > confirmation.Open &&
                                   confirmationBodyRatio >= minimumConfirmationBodyRatio &&
                                   confirmation.Close > previous.High + atr * breakoutBufferAtr &&
                                   (!parameters.RequireTrendSideClose || previous.Close >= emaPast);
        bool bearishConfirmation = confirmation.Close < confirmation.Open &&
                                   confirmationBodyRatio >= minimumConfirmationBodyRatio &&
                                   confirmation.Close < previous.Low - atr * breakoutBufferAtr &&
                                   (!parameters.RequireTrendSideClose || previous.Close <= emaPast);

        decimal rsi = CalculateRsi(candles, rsiPeriod);
        bool risingWave = priceDelta > 0 && slopeDegrees >= minimumSlopeDegrees;
        bool fallingWave = priceDelta < 0 && slopeDegrees >= minimumSlopeDegrees;

        int structureFirst = Math.Max(0, candles.Count - structureLookback - 2);
        int structureLast = candles.Count - 3;
        decimal priorResistance = decimal.MinValue;
        decimal priorSupport = decimal.MaxValue;
        for (int index = structureFirst; index <= structureLast; index++)
        {
            priorResistance = Math.Max(priorResistance, candles[index].High);
            priorSupport = Math.Min(priorSupport, candles[index].Low);
        }

        if (direction == TradeDirection.Long && risingWave && bullishConfirmation)
        {
            if (rsi >= overbought)
                return null;

            decimal stop = Math.Min(previous.Low, bandLower);
            decimal risk = confirmation.Close - stop;
            decimal room = priorResistance > confirmation.Close
                ? priorResistance - confirmation.Close
                : risk * minimumStructuralRiskReward;
            if (risk <= 0 || room < risk * minimumStructuralRiskReward)
                return null;

            decimal target = confirmation.Close + risk * minimumRiskReward;
            string strength = slopeDegrees >= strongSlopeDegrees ? "alta forte" : "alta";
            return new WavePullbackSignal(
                TradeDirection.Long,
                stop,
                target,
                room / risk,
                rsi,
                slopeDegrees,
                priorResistance > confirmation.Close ? priorResistance : null,
                $"onda {strength} ({slopeDegrees:F1} graus), toque e rompimento da máxima; RSI {rsi:F2}; RR {(room / risk):F2}:1");
        }

        if (direction == TradeDirection.Short && fallingWave && bearishConfirmation)
        {
            if (rsi <= oversold)
                return null;

            decimal stop = Math.Max(previous.High, bandUpper);
            decimal risk = stop - confirmation.Close;
            decimal room = priorSupport < confirmation.Close
                ? confirmation.Close - priorSupport
                : risk * minimumStructuralRiskReward;
            if (risk <= 0 || room < risk * minimumStructuralRiskReward)
                return null;

            decimal target = confirmation.Close - risk * minimumRiskReward;
            string strength = slopeDegrees >= strongSlopeDegrees ? "baixa forte" : "baixa";
            return new WavePullbackSignal(
                TradeDirection.Short,
                stop,
                target,
                room / risk,
                rsi,
                slopeDegrees,
                priorSupport < confirmation.Close ? priorSupport : null,
                $"onda {strength} ({slopeDegrees:F1} graus), toque e rompimento da mínima; RSI {rsi:F2}; RR {(room / risk):F2}:1");
        }

        return null;
    }

    private static decimal CalculateEma(IReadOnlyList<Candle> candles, int lastIndex, int period)
    {
        int firstIndex = Math.Max(0, lastIndex - period + 1);
        decimal smoothing = 2m / (period + 1m);
        decimal ema = candles[firstIndex].Close;

        for (int index = firstIndex + 1; index <= lastIndex; index++)
            ema = (candles[index].Close - ema) * smoothing + ema;

        return ema;
    }

    private static decimal CalculateAverageRange(IReadOnlyList<Candle> candles, int lastIndex, int count)
    {
        int firstIndex = Math.Max(0, lastIndex - count + 1);
        decimal total = 0;
        int samples = 0;
        for (int index = firstIndex; index <= lastIndex; index++)
        {
            total += Math.Max(0, candles[index].High - candles[index].Low);
            samples++;
        }

        return samples == 0 ? 0 : total / samples;
    }

    private static decimal CalculateAtr(IReadOnlyList<Candle> candles, int lastIndex, int count)
    {
        int firstIndex = Math.Max(1, lastIndex - count + 1);
        decimal total = 0;
        int samples = 0;
        for (int index = firstIndex; index <= lastIndex; index++)
        {
            decimal previousClose = candles[index - 1].Close;
            decimal trueRange = Math.Max(
                candles[index].High - candles[index].Low,
                Math.Max(
                    Math.Abs(candles[index].High - previousClose),
                    Math.Abs(candles[index].Low - previousClose)));
            total += Math.Max(0, trueRange);
            samples++;
        }

        return samples == 0 ? 0 : total / samples;
    }

    private static decimal CalculateRsi(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count < period + 1)
            return 50m;

        decimal gain = 0;
        decimal loss = 0;
        int first = candles.Count - period - 1;
        for (int index = first + 1; index <= first + period; index++)
        {
            decimal change = candles[index].Close - candles[index - 1].Close;
            if (change >= 0)
                gain += change;
            else
                loss -= change;
        }

        gain /= period;
        loss /= period;
        if (loss == 0)
            return gain == 0 ? 50m : 100m;

        return 100m - 100m / (1m + gain / loss);
    }
}

public sealed record WavePullbackSignal(
    TradeDirection Direction,
    decimal Stop,
    decimal Target,
    decimal PlannedRiskReward,
    decimal Rsi,
    decimal SlopeDegrees,
    decimal? StructuralTargetBoundary,
    string Reason);
