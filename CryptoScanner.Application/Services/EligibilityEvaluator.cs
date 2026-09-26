using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public static class EligibilityEvaluator
{
    public sealed class EligibilityResult
    {
        public bool FailedScore { get; init; }
        public bool FailedBreakout { get; init; }
        public bool FailedConsolidation { get; init; }
        public bool FailedVolumeSpike { get; init; }
        public bool FailedResistanceDistance { get; init; }
        public bool FailedDirection { get; init; }
        public bool FailedRiskReward { get; init; }
        public bool FailedInvalidLevels { get; init; }
        public bool FailedStopDistance { get; init; }
        public bool FailedStopDistanceTooHigh { get; init; }
        public bool FailedRiskRewardTooHigh { get; init; }
        public bool FailedBullTrap { get; init; }
        public bool FailedTrendConfirmation { get; init; }

        // Filtro experimental (12/2026) — ver EligibilityThresholds.RequireBearishMomentumConfirmed.
        public bool FailedMomentumFilter { get; init; }
        public bool FailedShortSideways { get; init; }
        public bool FailedShortScoreCeiling { get; init; }
        public bool FailedShortAdxInBear { get; init; }

        // Filtro experimental (22/08/2026) — ver EligibilityThresholds.BlockMeanReversionInBear.
        public bool FailedMeanReversionRegimeFilter { get; init; }

        // Filtro experimental (28/08/2026) — ver EligibilityThresholds.LimitAtrForMeanReversion.
        public bool FailedMeanReversionAtrFilter { get; init; }

        public bool IsEligible =>
            !FailedScore && !FailedBreakout && !FailedConsolidation &&
            !FailedVolumeSpike && !FailedResistanceDistance &&
            !FailedDirection && !FailedInvalidLevels && !FailedRiskReward && !FailedStopDistance &&
            !FailedStopDistanceTooHigh && !FailedRiskRewardTooHigh && !FailedBullTrap &&
            !FailedTrendConfirmation && !FailedMomentumFilter && !FailedShortSideways && !FailedShortScoreCeiling && !FailedShortAdxInBear && !FailedMeanReversionRegimeFilter &&
            !FailedMeanReversionAtrFilter;
    }

    public static string Describe(AssetAnalysis asset, string regime, EligibilityThresholds? thresholds = null)
    {
        var t = thresholds ?? EligibilityThresholds.Default;
        var result = Evaluate(asset, regime, t);
        var reasons = new List<string>();
        if(result.FailedScore) reasons.Add($"Score abaixo do mínimo {t.BuyOpportunityScore}, após ajuste de regime.");
        if(result.FailedBreakout) reasons.Add("Nenhum dos caminhos de entrada habilitados foi confirmado.");
        if(result.FailedConsolidation) reasons.Add("Sem consolidação anterior exigida para este caminho de entrada.");
        if(result.FailedVolumeSpike) reasons.Add($"Volume {asset.Volume.Spike:F2}× abaixo de {(regime=="BULL"?t.MinVolumeSpike:t.DefensiveMinVolumeSpike):F2}×.");
        decimal minimumDistance = MinimumTargetDistance(asset, t, asset.Direction);
        string expectedDirection = asset.Direction == TradeDirection.Short ? "BAIXA" : "ALTA";
        decimal describedTargetDistance = asset.Direction == TradeDirection.Short ? asset.Risk.SupportDistancePercent : asset.Risk.ResistanceDistancePercent;
        decimal describedStop = asset.Direction == TradeDirection.Short ? asset.Risk.Resistance : asset.Risk.Support;
        decimal describedTarget = asset.Direction == TradeDirection.Short ? asset.Risk.Support : asset.Risk.Resistance;
        if(result.FailedResistanceDistance) reasons.Add(minimumDistance == decimal.MaxValue
            ? "ATR indisponível para avaliar a distância mínima experimental."
            : $"Distância ao alvo {describedTargetDistance:F2}% abaixo de {minimumDistance:F2}%.");
        if(result.FailedDirection) reasons.Add($"Tendência não está em {expectedDirection}.");
        if(result.FailedInvalidLevels) reasons.Add($"Níveis inválidos: entrada {asset.Trend.Close:G}, stop {describedStop:G}, alvo {describedTarget:G}, TP1 {asset.Risk.TakeProfit1:G}, TP3 {asset.Risk.TakeProfit3:G}.");
        if(result.FailedRiskReward) reasons.Add($"R/R {asset.Risk.RiskReward:F2} abaixo de {t.MinRiskReward:F2}.");
        if(result.FailedStopDistance) reasons.Add($"Stop abaixo da distância mínima de {t.MinStopDistancePercent:F2}%.");
        if(result.FailedStopDistanceTooHigh) reasons.Add($"Stop acima da distância máxima de {t.MaxStopDistancePercent:F2}%.");
        if(result.FailedRiskRewardTooHigh) reasons.Add($"R/R acima do teto de {t.MaxRiskReward:F2}.");
        if(result.FailedBullTrap) reasons.Add("Armadilha de alta detectada.");
        if(result.FailedTrendConfirmation) reasons.Add("Confirmação de tendência ausente.");
        if(result.FailedMomentumFilter) reasons.Add("Momentum não confirmado.");
        if(result.FailedShortSideways) reasons.Add("Regime lateral bloqueia venda Short.");
        if(result.FailedShortScoreCeiling) reasons.Add($"Score Short acima do teto {t.MaxShortOpportunityScore:F0}.");
        if(result.FailedShortAdxInBear) reasons.Add($"ADX Short em BEAR acima do teto {t.MaxShortAdxInBear:F0}.");
        if(result.FailedMeanReversionRegimeFilter) reasons.Add("Regime bloqueia reversão à média.");
        if(result.FailedMeanReversionAtrFilter) reasons.Add("ATR bloqueia reversão à média.");
        return reasons.Count==0 ? "Critérios de entrada atendidos no candle fechado analisado." : string.Join("\n",reasons);
    }
    public static decimal MinimumTargetDistance(AssetAnalysis asset, EligibilityThresholds t, TradeDirection direction = TradeDirection.Long)
    {
        if (asset.Risk.Mode is RiskCalculationMode.MeanReversionScalp or RiskCalculationMode.BollingerReversal) return 0;
        if (t.MinimumTargetAtr is > 0) return asset.Trend.AtrPercent > 0 ? asset.Trend.AtrPercent * t.MinimumTargetAtr.Value : decimal.MaxValue;
        return asset.Risk.Mode switch
        {
            RiskCalculationMode.SwingWithPartialExits => t.MinResistanceDistancePartialExits,
            RiskCalculationMode.AtrBased or RiskCalculationMode.IntradayLocal => t.MinResistanceDistanceAtrMode,
            _ => t.MinResistanceDistance
        };
    }

    public static EligibilityResult Evaluate(AssetAnalysis asset, string marketRegime, EligibilityThresholds? thresholds = null, TradeDirection direction = TradeDirection.Long)
    {
        thresholds ??= EligibilityThresholds.Default;

        bool defensiveMode = marketRegime != "BULL";

        decimal opportunity = marketRegime switch
        {
            "BEAR" => asset.OpportunityScore - thresholds.BearRegimePenalty,
            "LATERAL" => asset.OpportunityScore - thresholds.SidewaysRegimePenalty,
            _ => asset.OpportunityScore
        };

        bool failedScore = opportunity < thresholds.BuyOpportunityScore;

        // asset.Setup.IsBreakout/IsShortTermBreakout já vêm calculados na direção certa
        // (AssetAnalyzer decide entre rompimento de alta ou de baixa) — não precisa checar
        // direção de novo aqui.
        bool passesClassicPaths = defensiveMode
            ? (asset.Setup.IsBreakout
                || asset.Setup.IsShortTermBreakout
                || asset.Setup.RelativeStrength >= thresholds.MinRelativeStrengthPercent)
            : asset.Setup.IsBreakout;

        // O repique é direcional: o AssetAnalyzer calcula a versão de alta ou de baixa
        // conforme o lado selecionado. Reversão à média continua disponível apenas para Long.
        bool passesPullbackBounce = thresholds.EnablePullbackBounce && asset.Setup.IsPullbackBounce;
        bool passesMeanReversionSetup = thresholds.EnableMeanReversionScalp && asset.Setup.IsMeanReversionSetup;
        bool passesBollingerReversal = thresholds.EnableBollingerReversal && asset.Setup.IsBollingerReversalSetup;

        // Caminho de RSI baixo (Fase 3, 16/08/2026) — mais uma opção no OR, atrás do
        // próprio Setup.IsLowRsiSetup já ter checado tendência+RSI+candle. Ver comentário
        // completo em SetupAnalysis.cs.
        bool passesLowRsiPath = thresholds.EnableLowRsiPath && asset.Setup.IsLowRsiSetup;

        bool failedBreakout = !(passesClassicPaths || passesPullbackBounce || passesMeanReversionSetup || passesBollingerReversal || passesLowRsiPath);

        bool failedConsolidation = defensiveMode ? false : !asset.Setup.IsConsolidating;
        if(thresholds.EntryStrategy != EntryStrategy.Legacy)
        {
            var strategy=thresholds.EntryStrategy==EntryStrategy.Auto?asset.EntryStrategy:thresholds.EntryStrategy;
            // Analyses carregadas de histórico/fixtures antigos não têm uma estratégia
            // explícita (Legacy). Nesse caso, preserve os caminhos clássicos calculados
            // acima; só force um único caminho quando a análise realmente o identificou.
            if (strategy != EntryStrategy.Legacy)
            {
                failedBreakout = strategy switch
                {
                    EntryStrategy.Breakout => !asset.Setup.IsBreakout,
                    EntryStrategy.Pullback => !asset.Setup.IsPullbackBounce,
                    EntryStrategy.MeanReversion => !asset.Setup.IsMeanReversionSetup,
                    _ => failedBreakout
                };
                failedConsolidation = strategy == EntryStrategy.Breakout && !asset.Setup.IsConsolidating;
            }
        }

        decimal volumeSpikeThreshold = defensiveMode
            ? thresholds.DefensiveMinVolumeSpike
            : thresholds.MinVolumeSpike;
        bool failedVolumeSpike = asset.Volume.Spike < volumeSpikeThreshold;

        decimal effectiveMinResistanceDistance = MinimumTargetDistance(asset, thresholds, direction);
        decimal targetDistance = direction == TradeDirection.Short
            ? asset.Risk.SupportDistancePercent
            : asset.Risk.ResistanceDistancePercent;
        decimal stopDistance = direction == TradeDirection.Short
            ? asset.Risk.ResistanceDistancePercent
            : asset.Risk.SupportDistancePercent;
        bool failedResistanceDistance = targetDistance < effectiveMinResistanceDistance;

        // Fase 1 do lado de venda: Long exige tendência de ALTA, Short exige tendência de BAIXA.
        bool failedDirection = direction == TradeDirection.Long
            ? asset.Trend.Direction != "ALTA"
            : asset.Trend.Direction != "BAIXA";

        // A positive ratio cannot make invalid or inverted levels tradable.
        bool invalidLevels = asset.Trend.Close <= 0 || asset.Risk.Support <= 0 || asset.Risk.Support >= asset.Trend.Close ||
            asset.Risk.Resistance <= asset.Trend.Close ||
            (direction == TradeDirection.Long && asset.Risk.TakeProfit1.HasValue &&
                (asset.Risk.TakeProfit1 <= asset.Trend.Close || asset.Risk.TakeProfit1 >= asset.Risk.Resistance ||
                 !asset.Risk.TakeProfit3.HasValue || asset.Risk.TakeProfit3 <= asset.Risk.Resistance));
        bool failedRiskReward = !invalidLevels && asset.Risk.RiskReward < thresholds.MinRiskReward;
        bool failedStopDistance = stopDistance < thresholds.MinStopDistancePercent;
        bool failedStopDistanceTooHigh = stopDistance > thresholds.MaxStopDistancePercent;
        bool failedRiskRewardTooHigh = asset.Risk.RiskReward > thresholds.MaxRiskReward;

        // Bull Trap (rompimento de alta falso) é o risco específico de Long; o espelho pra
        // Short é o Bear Trap (rompimento de baixa falso, já calculado em Structure, mas
        // nunca usado até agora). O nome do campo (FailedBullTrap) continua o mesmo por
        // simplicidade — evita renomear em cascata por vários outros arquivos — mas o que
        // ele mede muda conforme a direção.
        bool failedBullTrap = direction == TradeDirection.Long
            ? asset.Structure.IsBullTrap
            : asset.Structure.IsBearTrap;

        // Fase A do lado de venda — segundo pilar (EMAs alinhadas e caindo). Long não tem
        // esse portão (nunca teve exigência de alinhamento de EMA como critério obrigatório
        // — só entra no Score geral); Short exige confirmação explícita antes de ser elegível.
        // Reversão de Bollinger é isenta: o próprio propósito dela é pegar a virada ANTES da
        // baixa estar confirmada em EMA — exigir isso aqui contradiria o setup. O filtro
        // "não andar na banda" (dentro de IsBollingerReversalSetup) já cumpre um papel
        // protetor parecido, com lógica mais adequada a um setup de reversão.
        bool failedTrendConfirmation =
            false; // TESTE DIAGNÓSTICO — portão de EMA temporariamente desligado, pra isolar
                   // se ele é o gargalo (Rompimento clássico caiu de 7 pra 3 operações depois
                   // que Estrutura+EMA entraram — precisa saber qual dos dois é responsável).
                   // Linha original, comentada abaixo — reativar depois do diagnóstico:
                   // direction == TradeDirection.Short &&
                   // asset.Risk.Mode != RiskCalculationMode.BollingerReversal &&
                   // !asset.Trend.IsBearishTrendConfirmed;

        // Filtro experimental (12/2026) — exige Momentum Baixista confirmado (RSI com topo
        // mais baixo acompanhando o topo de preço mais baixo), só pro Bollinger Reversal
        // Short. Investigação: no teste agregado (101 trades), o subconjunto com Momentum
        // confirmado teve PF 1,38 vs 1,06 sem confirmação — testando se formalizar esse
        // filtro melhora o resultado da amostra completa. Default false (ver
        // EligibilityThresholds.RequireBearishMomentumConfirmed) — não altera nenhum
        // resultado já validado até ser explicitamente habilitado.
        bool failedMomentumFilter =
            thresholds.RequireBearishMomentumConfirmed &&
            direction == TradeDirection.Short &&
            !asset.Trend.IsBearishMomentumConfirmed;

        bool failedShortSideways =
            thresholds.BlockShortInSideways &&
            direction == TradeDirection.Short &&
            marketRegime == "LATERAL";

        // O teto usa o Score bruto exibido/exportado no ranking. O piso continua
        // usando o Score efetivo após a penalidade de regime, como antes.
        bool failedShortScoreCeiling =
            direction == TradeDirection.Short &&
            asset.OpportunityScore > thresholds.MaxShortOpportunityScore;

        bool failedShortAdxInBear =
            direction == TradeDirection.Short &&
            marketRegime == "BEAR" &&
            asset.Trend.Adx > thresholds.MaxShortAdxInBear;

        // Filtro experimental (22/08/2026) — ver EligibilityThresholds.BlockMeanReversionInBear.
        bool failedMeanReversionRegimeFilter =
            thresholds.BlockMeanReversionInBear &&
            asset.Risk.Mode == RiskCalculationMode.MeanReversionScalp &&
            marketRegime == "BEAR";

        // Filtro experimental (28/08/2026) — ver EligibilityThresholds.LimitAtrForMeanReversion.
        bool failedMeanReversionAtrFilter =
            thresholds.LimitAtrForMeanReversion &&
            asset.Risk.Mode == RiskCalculationMode.MeanReversionScalp &&
            asset.Trend.AtrPercent > 4m;

        return new EligibilityResult
        {
            FailedScore = failedScore,
            FailedBreakout = failedBreakout,
            FailedConsolidation = failedConsolidation,
            FailedVolumeSpike = failedVolumeSpike,
            FailedResistanceDistance = failedResistanceDistance,
            FailedDirection = failedDirection,
            FailedRiskReward = failedRiskReward,
            FailedInvalidLevels = invalidLevels,
            FailedStopDistance = failedStopDistance,
            FailedStopDistanceTooHigh = failedStopDistanceTooHigh,
            FailedRiskRewardTooHigh = failedRiskRewardTooHigh,
            FailedBullTrap = failedBullTrap,
            FailedTrendConfirmation = failedTrendConfirmation,
            FailedMomentumFilter = failedMomentumFilter,
            FailedShortSideways = failedShortSideways,
            FailedShortScoreCeiling = failedShortScoreCeiling,
            FailedShortAdxInBear = failedShortAdxInBear,
            FailedMeanReversionRegimeFilter = failedMeanReversionRegimeFilter,
            FailedMeanReversionAtrFilter = failedMeanReversionAtrFilter
        };
    }
}
