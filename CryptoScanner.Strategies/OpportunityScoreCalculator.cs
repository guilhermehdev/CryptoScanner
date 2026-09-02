using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Strategies;

public static class OpportunityScoreCalculator
{
    public static decimal Calculate(AssetAnalysis analysis, TradeDirection direction = TradeDirection.Long)
    {
        decimal structureScore = direction == TradeDirection.Long ? analysis.Structure.Score : 100m - analysis.Structure.Score;
        decimal candleScore = direction == TradeDirection.Long ? analysis.Candle.Score : 100m - analysis.Candle.Score;

        decimal score =
            analysis.Trend.Score * ScannerSettings.TrendWeight +
            analysis.Volume.Score * ScannerSettings.VolumeWeight +
            structureScore * ScannerSettings.StructureWeight +
            candleScore * ScannerSettings.CandleWeight +
            analysis.Setup.Score * ScannerSettings.SetupWeight +
            analysis.Trend.MomentumScore * ScannerSettings.MomentumWeight +
            analysis.Trend.VolatilityScore * ScannerSettings.VolatilityWeight +
            analysis.Trend.TrendStrengthScore * ScannerSettings.TrendStrengthWeight;

        score += (analysis.RetailFlowScore - 50m) * 0.10m;
        return Math.Round(Math.Clamp(score, 0m, 100m), 2);
    }
}