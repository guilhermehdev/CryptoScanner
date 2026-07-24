using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Indicators.Indicators
{
    using CryptoScanner.Core.Models;

    public static class OpportunityScoreCalculator
    {
        public static decimal Calculate(AssetScore asset)
        {
            decimal score = asset.FinalScore;

            // Tendência
            if (asset.TrendDirection == "ALTA")
                score += 8;
            else
                score -= 15;

            // Rompimento
            if (asset.IsBreakout)
                score += 8;

            // Consolidação
            if (asset.IsConsolidating)
                score += 6;

            // Volume
            if (asset.VolumeSpike >= 2m)
                score += 10;
            else if (asset.VolumeSpike >= 1.5m)
                score += 5;
            else if (asset.VolumeSpike < 1m)
                score -= 10;

            // Espaço até resistência
            if (asset.ResistanceDistance >= 30)
                score += 12;
            else if (asset.ResistanceDistance >= 20)
                score += 8;
            else if (asset.ResistanceDistance >= 10)
                score += 4;
            else
                score -= 10;

            // Risk / Reward
            if (asset.RiskReward >= 5)
                score += 12;
            else if (asset.RiskReward >= 3)
                score += 8;
            else if (asset.RiskReward >= 2)
                score += 4;
            else
                score -= 15;

            // Rejeição
            if (asset.RejectionScore >= 0.60m)
                score -= 20;
            else if (asset.RejectionScore >= 0.40m)
                score -= 10;
            else if (asset.RejectionScore >= 0.20m)
                score -= 5;

            if (asset.StrongUptrend)
                score += 10;

            if (asset.BreakOfStructure)
                score += 15;

            if (asset.ChangeOfCharacter)
                score += 20;

            if (asset.StrongDowntrend)
                score -= 25;

            score += (asset.CandleScore - 50) * 0.40m;
            score = Math.Clamp(score, 0m, 100m);

            return Math.Round(score, 2);
        }
    }
}
