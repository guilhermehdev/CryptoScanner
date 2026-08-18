using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using System.Collections.Generic;

namespace CryptoScanner.Application.Services;

public static class AssetScoreFactory
{
    public static AssetScore Create(AssetAnalysis analysis, string marketRegime, IReadOnlySet<string> favoriteSymbols, EligibilityThresholds? thresholds = null) => new()
    {
        Symbol = analysis.Symbol,
        Close = analysis.Trend.Close,
        Score = analysis.OpportunityScore,
        OpportunityScore = analysis.OpportunityScore,
        PreviousScore = analysis.PreviousScore,
        ScoreVariation = analysis.ScoreVariation,
        Resistance = analysis.Risk.Resistance,
        TakeProfit1 = analysis.Risk.TakeProfit1,
        TakeProfit3 = analysis.Risk.TakeProfit3,
        VolumeSpike = analysis.Volume.Spike,
        ResistanceDistance = analysis.Risk.ResistanceDistancePercent,
        SupportDistance = analysis.Risk.SupportDistancePercent,
        RiskReward = analysis.Risk.RiskReward,
        TrendDirection = analysis.Trend.Direction,
        IsBreakout = analysis.Setup.IsBreakout,
        IsShortTermBreakout = analysis.Setup.IsShortTermBreakout,
        RelativeStrength = analysis.Setup.RelativeStrength,
        IsConsolidating = analysis.Setup.IsConsolidating,
        IsEliteSetup = analysis.IsEliteSetup,
        HasExhaustion = analysis.Volume.HasExhaustion,
        PatternName = analysis.Candle.PatternName,
        BreakoutSource = DetermineBreakoutSource(analysis),
        MarketRegime = marketRegime,
        SmartMoneyLabel = analysis.Structure.SmartMoneyLabel,
        IsBullTrap = analysis.Structure.IsBullTrap,
        IsBearTrap = analysis.Structure.IsBearTrap,
        IsEligible = EligibilityEvaluator.Evaluate(analysis, marketRegime, thresholds).IsEligible,
        IsFavorite = favoriteSymbols.Contains(analysis.Symbol),
        TrendScore = analysis.Trend.Score,
        StructureScore = analysis.Structure.Score,
        VolumeScore = analysis.Volume.Score,
        CandleScore = analysis.Candle.Score,
        SetupScore = analysis.Setup.Score,
        MomentumScore = analysis.Trend.MomentumScore,
        VolatilityScore = analysis.Trend.VolatilityScore,
        TrendStrengthScore = analysis.Trend.TrendStrengthScore,
        Support = analysis.Risk.Support,
        Rsi = analysis.Trend.Rsi,
        Adx = analysis.Trend.Adx,
        AtrPercent = analysis.Trend.AtrPercent,
        EmaDistanceAtr = analysis.Setup.EmaDistanceAtr,
        SwingUsageAtr = analysis.Setup.SwingUsageAtr,
        VolumeImbalance = analysis.Volume.Imbalance,
    };

    // Visibilidade alterada de private pra internal (16/08/2026) — reaproveitado por
    // StrategyBacktester.cs pra popular o mesmo BreakoutSource no resultado do Backtest
    // (Fase 3 do roadmap: análise por fator precisa do mesmo contexto que o SignalHistory
    // ao vivo já tem, mas com a amostra grande do Backtest). Mesma namespace
    // (CryptoScanner.Application.Services), sem necessidade de using adicional.
    internal static string DetermineBreakoutSource(AssetAnalysis analysis)
    {
        if (analysis.Setup.IsBreakout) return "Clássico";
        if (analysis.Setup.IsShortTermBreakout) return "Curto Prazo";
        if (analysis.Setup.RelativeStrength >= ScannerSettings.MinRelativeStrengthPercent) return "Força Rel.";
        return "";
    }
}