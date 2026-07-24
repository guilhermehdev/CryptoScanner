using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Strategies;

public static class OpportunityScoreCalculator
{
    public static decimal Calculate(AssetAnalysis analysis)
    {
        decimal score =
            analysis.Trend.Score * ScannerSettings.TrendWeight +
            analysis.Volume.Score * ScannerSettings.VolumeWeight +
            analysis.Structure.Score * ScannerSettings.StructureWeight +
            analysis.Candle.Score * ScannerSettings.CandleWeight +
            analysis.Setup.Score * ScannerSettings.SetupWeight;

        return Math.Round(Math.Clamp(score, 0m, 100m), 2);
    }
}
