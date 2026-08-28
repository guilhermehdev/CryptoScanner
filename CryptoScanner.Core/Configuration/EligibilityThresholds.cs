namespace CryptoScanner.Core.Configuration;

public sealed class EligibilityThresholds
{
    public required decimal BuyOpportunityScore { get; init; }
    public required decimal BearRegimePenalty { get; init; }
    public required decimal SidewaysRegimePenalty { get; init; }
    public required decimal MinVolumeSpike { get; init; }
    public required decimal DefensiveMinVolumeSpike { get; init; }
    public required decimal MinResistanceDistance { get; init; }
    public required bool EnableMultiTimeframe { get; init; }

    // Limiar separado pro modo ATR — Res% aqui mede volatilidade (ATR% × multiplicador),
    // não distância estrutural até um topo real. Precisa de escala diferente.
    public required decimal MinResistanceDistanceAtrMode { get; init; }

    public required decimal MinRiskReward { get; init; }
    public required decimal MinRelativeStrengthPercent { get; init; }
    public required decimal MinStopDistancePercent { get; init; }

    // Teto de distância de stop — NÃO-obrigatório de propósito (default = sem limite), pra
    // não quebrar nenhuma construção existente de EligibilityThresholds espalhada pelo app.
    // Investigado depois de um SL absurdamente longe (HEIUSDT, ~81% de distância) escapar
    // ileso dos filtros de proporção — SL e TP estavam esticados na mesma escala, então o
    // RR parecia razoável mesmo os valores absolutos sendo um absurdo.
    public decimal MaxStopDistancePercent { get; init; } = decimal.MaxValue;

    public required decimal MaxRiskReward { get; init; }
    public required bool EnablePullbackBounce { get; init; }
    public required bool EnableBollingerScoring { get; init; }
    public required bool EnableVolatilityScoringPhaseB { get; init; }
    public required decimal MinResistanceDistancePartialExits { get; init; }

    // Reversão à média (Scalp) — NÃO-obrigatório de propósito, mesmo padrão do
    // MaxStopDistancePercent acima: evita quebrar toda construção existente de
    // EligibilityThresholds espalhada pelo app. Default false — desligado até validar.
    public bool EnableMeanReversionScalp { get; init; } = false;

    // Reversão de Bollinger (Fase A do lado de venda) — mesmo padrão não-obrigatório acima.
    public bool EnableBollingerReversal { get; init; } = false;

    // Filtro experimental (12/2026) — exige TrendAnalysis.IsBearishMomentumConfirmed como
    // portão de elegibilidade, só afeta BollingerReversal + Short (ver EligibilityEvaluator).
    // Investigação: no teste agregado (101 trades), o subconjunto com Momentum confirmado
    // teve PF 1,38 vs 1,06 no subconjunto sem confirmação — sinal de que pode ser um filtro
    // de qualidade real. Default false, mesmo padrão não-obrigatório dos outros experimentais
    // acima — não altera nenhum resultado já validado até ser explicitamente habilitado.
    public bool RequireBearishMomentumConfirmed { get; init; } = false;

    // Caminho de RSI baixo (Fase 3, 16/08/2026) — ver comentário completo em
    // SetupAnalysis.IsLowRsiSetup. Mesmo padrão não-obrigatório dos outros experimentais
    // acima — default false, só entra no OR de elegibilidade quando explicitamente ligado.
    public bool EnableLowRsiPath { get; init; } = false;

    // Filtro experimental (22/08/2026) — bloqueia Reversão à Média (Scalp) em regime BEAR.
    // Investigação: numa amostra de 1.585 trades (limiares soltos), regime BEAR sozinho
    // carregava todo o prejuízo do agregado (PF 0,90 geral vs PF 1,34 fora do BEAR, 483
    // trades, sem outlier dominando) — comprar recuo dentro de tendência de alta não
    // funciona quando o recuo tende a continuar caindo, como em bear market. Mesmo padrão
    // não-obrigatório dos outros experimentais — default false, não altera nenhum
    // resultado até ser explicitamente habilitado.
    public bool BlockMeanReversionInBear { get; init; } = false;

    // Filtro experimental (28/08/2026) — teto de ATR% pro Reversão à Média (Scalp).
    // Investigação: comparando período ruim (2020-2022, ATR% médio 4,85, 78% saída por
    // SL) vs período bom (2024-2025, ATR% médio 2,94, 45,8% SL) — volatilidade alta
    // parece causar stop-out antes da reversão acontecer (stop é múltiplo pequeno de
    // ATR). Teto fixo de 4% (hardcoded no EligibilityEvaluator) escolhido por já ter
    // aparecido como limiar ruim também na análise geral de fatores do Compra (amostra
    // de 3.885 trades). Default false.
    public bool LimitAtrForMeanReversion { get; init; } = false;

    public static readonly EligibilityThresholds Default = new()
    {
        BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
        BearRegimePenalty = ScannerSettings.BearRegimePenalty,
        SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
        MinVolumeSpike = ScannerSettings.MinVolumeSpike,
        DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
        MinResistanceDistance = ScannerSettings.MinResistanceDistance,
        MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // provisório — modo ATR ainda não é usado ao vivo
        MinRiskReward = ScannerSettings.MinRiskReward,
        MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
        MinStopDistancePercent = 0m,
        MaxRiskReward = decimal.MaxValue,
        EnablePullbackBounce = false,
        EnableBollingerScoring = false,
        EnableVolatilityScoringPhaseB = false,
        MinResistanceDistancePartialExits = ScannerSettings.MinResistanceDistance, // provisório — a calibrar via comparador
        EnableMultiTimeframe = false,
    };
}