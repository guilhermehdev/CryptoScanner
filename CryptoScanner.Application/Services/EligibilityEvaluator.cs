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
        public bool FailedStopDistance { get; init; }
        public bool FailedStopDistanceTooHigh { get; init; }
        public bool FailedRiskRewardTooHigh { get; init; }
        public bool FailedBullTrap { get; init; }
        public bool FailedTrendConfirmation { get; init; }

        // Filtro experimental (12/2026) — ver EligibilityThresholds.RequireBearishMomentumConfirmed.
        public bool FailedMomentumFilter { get; init; }

        public bool IsEligible =>
            !FailedScore && !FailedBreakout && !FailedConsolidation &&
            !FailedVolumeSpike && !FailedResistanceDistance &&
            !FailedDirection && !FailedRiskReward && !FailedStopDistance &&
            !FailedStopDistanceTooHigh && !FailedRiskRewardTooHigh && !FailedBullTrap &&
            !FailedTrendConfirmation && !FailedMomentumFilter;
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

        // Caminho A e Reversão à Média são Long-only na Fase 1 do lado de venda — IsPullbackBounce
        // e IsMeanReversionSetup já vêm sempre false quando a direção é Short (ver AssetAnalyzer),
        // então essas duas linhas já ficam naturalmente neutralizadas pra Short, sem checar aqui.
        bool passesPullbackBounce = thresholds.EnablePullbackBounce && asset.Setup.IsPullbackBounce;
        bool passesMeanReversionSetup = thresholds.EnableMeanReversionScalp && asset.Setup.IsMeanReversionSetup;
        bool passesBollingerReversal = thresholds.EnableBollingerReversal && asset.Setup.IsBollingerReversalSetup;

        // Caminho de RSI baixo (Fase 3, 16/08/2026) — mais uma opção no OR, atrás do
        // próprio Setup.IsLowRsiSetup já ter checado tendência+RSI+candle. Ver comentário
        // completo em SetupAnalysis.cs.
        bool passesLowRsiPath = thresholds.EnableLowRsiPath && asset.Setup.IsLowRsiSetup;

        bool failedBreakout = !(passesClassicPaths || passesPullbackBounce || passesMeanReversionSetup || passesBollingerReversal || passesLowRsiPath);

        bool failedConsolidation = defensiveMode ? false : !asset.Setup.IsConsolidating;

        decimal volumeSpikeThreshold = defensiveMode
            ? thresholds.DefensiveMinVolumeSpike
            : thresholds.MinVolumeSpike;
        bool failedVolumeSpike = asset.Volume.Spike < volumeSpikeThreshold;

        decimal effectiveMinResistanceDistance = asset.Risk.Mode switch
        {
            RiskCalculationMode.AtrBased => thresholds.MinResistanceDistanceAtrMode,
            RiskCalculationMode.SwingWithPartialExits => thresholds.MinResistanceDistancePartialExits,
            // Reversão à média mira deliberadamente um alvo próximo (volta pra EMA21) —
            // exigir uma distância mínima estrutural aqui contradiria a própria lógica do
            // setup. Sem piso nesse modo.
            RiskCalculationMode.MeanReversionScalp => 0m,
            // Mesmo motivo — TP1 é a banda média, deliberadamente próxima do preço.
            RiskCalculationMode.BollingerReversal => 0m,
            _ => thresholds.MinResistanceDistance
        };
        bool failedResistanceDistance = asset.Risk.ResistanceDistancePercent < effectiveMinResistanceDistance;

        // Fase 1 do lado de venda: Long exige tendência de ALTA, Short exige tendência de BAIXA.
        bool failedDirection = direction == TradeDirection.Long
            ? asset.Trend.Direction != "ALTA"
            : asset.Trend.Direction != "BAIXA";

        bool failedRiskReward = asset.Risk.RiskReward < thresholds.MinRiskReward;
        bool failedStopDistance = asset.Risk.SupportDistancePercent < thresholds.MinStopDistancePercent;
        bool failedStopDistanceTooHigh = asset.Risk.SupportDistancePercent > thresholds.MaxStopDistancePercent;
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
            asset.Risk.Mode == RiskCalculationMode.BollingerReversal &&
            !asset.Trend.IsBearishMomentumConfirmed;

        return new EligibilityResult
        {
            FailedScore = failedScore,
            FailedBreakout = failedBreakout,
            FailedConsolidation = failedConsolidation,
            FailedVolumeSpike = failedVolumeSpike,
            FailedResistanceDistance = failedResistanceDistance,
            FailedDirection = failedDirection,
            FailedRiskReward = failedRiskReward,
            FailedStopDistance = failedStopDistance,
            FailedStopDistanceTooHigh = failedStopDistanceTooHigh,
            FailedRiskRewardTooHigh = failedRiskRewardTooHigh,
            FailedBullTrap = failedBullTrap,
            FailedTrendConfirmation = failedTrendConfirmation,
            FailedMomentumFilter = failedMomentumFilter
        };
    }
}