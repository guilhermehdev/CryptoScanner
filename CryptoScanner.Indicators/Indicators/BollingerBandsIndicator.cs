using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators;

public static class BollingerBandsIndicator
{
    /// <summary>
    /// Calcula as Bandas de Bollinger clássicas: banda média (SMA), superior e inferior
    /// (SMA ± multiplicador × desvio padrão), e a largura da banda em percentual do preço médio.
    /// Segue a mesma convenção dos outros indicadores do projeto: uma lista do mesmo tamanho
    /// que os candles de entrada, com null nos primeiros (period-1) índices, que ainda não
    /// têm dado suficiente pra calcular.
    /// </summary>
    public static (List<decimal?> Middle, List<decimal?> Upper, List<decimal?> Lower, List<decimal?> BandWidthPercent) Calculate(
        List<Candle> candles, int period = 20, decimal stdDevMultiplier = 2m)
    {
        int count = candles.Count;
        var middle = new List<decimal?>(new decimal?[count]);
        var upper = new List<decimal?>(new decimal?[count]);
        var lower = new List<decimal?>(new decimal?[count]);
        var bandWidth = new List<decimal?>(new decimal?[count]);

        for (int i = period - 1; i < count; i++)
        {
            decimal sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += candles[j].Close;

            decimal sma = sum / period;

            decimal varianceSum = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                decimal diff = candles[j].Close - sma;
                varianceSum += diff * diff;
            }

            decimal variance = varianceSum / period;
            decimal stdDev = (decimal)Math.Sqrt((double)variance);

            decimal upperBand = sma + (stdDevMultiplier * stdDev);
            decimal lowerBand = sma - (stdDevMultiplier * stdDev);

            middle[i] = sma;
            upper[i] = upperBand;
            lower[i] = lowerBand;
            bandWidth[i] = sma != 0 ? ((upperBand - lowerBand) / sma) * 100m : 0m;
        }

        return (middle, upper, lower, bandWidth);
    }
}