using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public static class LlmSnapshotFactory
{
    public static LlmAnalysisSnapshot Create(
        AssetScore asset,
        ScanProfile profile,
        EligibilityThresholds thresholds,
        DateTime utcNow)
    {
        decimal entry = asset.LivePrice ?? asset.Close;
        bool isShort = asset.Direction == TradeDirection.Short;
        decimal stop = isShort ? asset.Resistance : asset.Support;
        decimal target = isShort ? asset.Support : asset.Resistance;
        decimal? tp1 = asset.TakeProfit1 ?? target;
        bool levelsAreValid = entry > 0 && stop > 0 && target > 0 &&
            (isShort ? stop > entry && target < entry : stop < entry && target > entry) &&
            (!tp1.HasValue || (isShort ? tp1 < entry : tp1 > entry));

        string volumeStatus = asset.VolumeSpike < 1m ? "ABAIXO_DA_MEDIA" :
            asset.VolumeSpike < thresholds.MinVolumeSpike ? "ABAIXO_DO_MINIMO_DA_ESTRATEGIA" :
            asset.VolumeSpike < 1.5m ? "MINIMO_ATENDIDO" :
            asset.VolumeSpike < 2m ? "ACIMA_DA_MEDIA" : "FORTE";

        return new LlmAnalysisSnapshot
        {
            Version = LlmAnalysisSnapshot.CurrentVersion,
            CapturedAtUtc = utcNow,
            Symbol = asset.Symbol,
            Profile = profile.Name,
            CandleInterval = profile.CandleInterval,
            ScannerSignal = asset.DisplaySignal,
            Direction = isShort ? "SHORT" : "LONG",
            Trend = asset.TrendDirection,
            MarketRegime = asset.MarketRegime,
            IsEligible = asset.IsEligible,
            CanRecommendTrade = asset.IsEligible && levelsAreValid,
            EligibilityDetails = asset.EligibilityDetails,
            VolumeStatus = volumeStatus,
            AnalysisPrice = asset.Close,
            CurrentPrice = entry,
            Score = asset.Score,
            Rsi = asset.Rsi,
            Adx = asset.Adx,
            AtrPercent = asset.AtrPercent,
            VolumeSpike = asset.VolumeSpike,
            BuyingPressureScore = asset.BuyingPressureScore,
            RelativeStrength = asset.RelativeStrength,
            RiskReward = asset.RiskReward,
            Support = asset.Support,
            Resistance = asset.Resistance,
            ExpectedEntry = levelsAreValid ? entry : null,
            ExpectedStop = levelsAreValid ? stop : null,
            ExpectedTp1 = levelsAreValid ? tp1 : null,
            ExpectedTp2 = levelsAreValid ? target : null,
            IsBullTrap = asset.IsBullTrap,
            IsBearTrap = asset.IsBearTrap,
            PatternName = asset.PatternName,
            StrategyRules = $"Score mínimo {thresholds.BuyOpportunityScore:F0}; volume mínimo {thresholds.MinVolumeSpike:F2}; R/R mínimo {thresholds.MinRiskReward:F2}."
        };
    }
}
