using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Strategies;

public static class OpportunityScoreCalculator
{
    public static decimal Calculate(AssetAnalysis analysis, TradeDirection direction = TradeDirection.Long)
    {
        // Trend.Score, Trend.MomentumScore, Trend.TrendStrengthScore e Volume.Score já
        // chegam calculados certos pra direção testada (AssetAnalyzer decide entre a
        // versão normal e o espelho de baixa antes de montar AssetAnalysis) — não precisam
        // de tratamento aqui. Volatility é direção-neutro (só mede magnitude), também sem
        // tratamento. Só Structure e Candle são simétricos por natureza (0-100, onde 0 já
        // significa "extremo de baixa") — pra eles, inverter aqui é mais simples do que
        // calcular duas vezes lá atrás.
        decimal structureScore = direction == TradeDirection.Long
            ? analysis.Structure.Score
            : 100m - analysis.Structure.Score;
        decimal candleScore = direction == TradeDirection.Long
            ? analysis.Candle.Score
            : 100m - analysis.Candle.Score;

        decimal score =
            analysis.Trend.Score * ScannerSettings.TrendWeight +
            analysis.Volume.Score * ScannerSettings.VolumeWeight +
            structureScore * ScannerSettings.StructureWeight +
            candleScore * ScannerSettings.CandleWeight +
            analysis.Setup.Score * ScannerSettings.SetupWeight +
            analysis.Trend.MomentumScore * ScannerSettings.MomentumWeight +
            analysis.Trend.VolatilityScore * ScannerSettings.VolatilityWeight +
            analysis.Trend.TrendStrengthScore * ScannerSettings.TrendStrengthWeight;

        return Math.Round(Math.Clamp(score, 0m, 100m), 2);
    }
}