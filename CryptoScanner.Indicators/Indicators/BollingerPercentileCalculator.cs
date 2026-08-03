namespace CryptoScanner.Indicators.Indicators;

public static class BollingerPercentileCalculator
{
    /// <summary>
    /// Calcula o percentil da largura de banda ATUAL em relação aos últimos N valores
    /// válidos (padrão 200). Ex.: 18% significa que a banda está mais estreita que 82%
    /// do histórico recente.
    /// </summary>
    public static decimal? CalculateCurrentPercentile(List<decimal?> bandWidth, int lookback = 200)
    {
        var validValues = bandWidth.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        if (validValues.Count == 0)
            return null;

        decimal current = validValues[^1];

        var window = validValues.Count > lookback
            ? validValues.GetRange(validValues.Count - lookback, lookback)
            : validValues;

        if (window.Count < 20) // histórico curto demais pra um percentil confiável
            return null;

        int countBelowOrEqual = window.Count(v => v <= current);
        return (decimal)countBelowOrEqual / window.Count * 100m;
    }
}