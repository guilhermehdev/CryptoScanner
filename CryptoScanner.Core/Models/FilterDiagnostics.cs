namespace CryptoScanner.Core.Models;

public sealed class FilterDiagnostics
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = "scan-funnel-v5";
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public int Requested { get; set; }
    public int SignalsSaved { get; set; }
    public int EntryRejected { get; set; }
    // Rejeições que só aparecem depois de a elegibilidade passar (preço de entrada,
    // slippage ou R/R recalculado). Mantém o total acima para compatibilidade e abre
    // o funil da última etapa no relatório do backtest.
    public Dictionary<string,int> EntryRejectionReasons { get; set; } = new();
    public DateTime? BacktestStartUtc { get; set; }
    public DateTime? BacktestEndUtc { get; set; }
    public string Profile { get; set; } = "";
    public int BreakoutTriggers { get; set; }
    public int PullbackTriggers { get; set; }
    public Dictionary<string,StrategyDiagnostics> Strategies { get; set; } = new();
    public string StrategySummary => string.Join("\n",Strategies.OrderBy(x=>x.Key).Select(x=>$"{StrategyDiagnostics.Label(x.Key)}: avaliados {x.Value.Evaluated}; gatilhos {x.Value.Triggered}; elegíveis antes da entrada {x.Value.Eligible}; bloqueios entre gatilhos: " +
        string.Join(", ",x.Value.Rejections.Select(p=>$"{StrategyDiagnostics.Label(p.Key)}={p.Value}")) + "; posição na zona estrutural: " +
        string.Join(", ",x.Value.TargetPositions.Select(p=>$"{p.Key}={p.Value}"))));
    public Dictionary<string,string> Errors { get; set; } = new();
    public Dictionary<string,int> CandidateTypes { get; set; } = new();
    public Dictionary<string,List<string>> OnlyBlockedBy { get; set; } = new();
    public int TotalAnalyzed { get; set; }
    public int PassedAll { get; set; }

    public int FailedScore { get; set; }
    public int FailedBreakout { get; set; }
    public int FailedConsolidation { get; set; }
    public int FailedVolumeSpike { get; set; }
    public int FailedResistanceDistance { get; set; }
    public int FailedDirection { get; set; }
    public int FailedRiskReward { get; set; }
    public int FailedInvalidLevels { get; set; }
    public string MarketRegime { get; set; } = "";
    public CryptoScanner.Core.Configuration.EligibilityThresholds? Thresholds { get; set; }
    public List<CryptoScanner.Core.Models.Analysis.AssetAnalysis> Analyses { get; set; } = new();
    public int FailedStopDistance { get; set; }
    public int FailedStopDistanceTooHigh { get; set; }
    public int FailedRiskRewardTooHigh { get; set; }
    public int FailedBullTrap { get; set; }
    public int FailedTrendConfirmation { get; set; }

    // Filtro experimental (12/2026) — ver EligibilityThresholds.RequireBearishMomentumConfirmed.
    public int FailedMomentumFilter { get; set; }
    public int FailedShortSideways { get; set; }
    public int FailedShortScoreCeiling { get; set; }
    public int FailedShortAdxInBear { get; set; }

    // Filtro experimental (22/08/2026) — ver EligibilityThresholds.BlockMeanReversionInBear.
    public int FailedMeanReversionRegimeFilter { get; set; }

    // Filtro experimental (28/08/2026) — ver EligibilityThresholds.LimitAtrForMeanReversion.
    public int FailedMeanReversionAtrFilter { get; set; }

    public int SkippedDuplicateToday { get; set; }

    public string EntryRejectionSummary => EntryRejectionReasons.Count == 0
        ? "sem detalhe"
        : string.Join(", ", EntryRejectionReasons.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));

    public string Summary =>
        $"Score: {FailedScore} | Sem breakout: {FailedBreakout} | Sem consol.: {FailedConsolidation} | " +
        $"Vol. spike: {FailedVolumeSpike} | Dist. resist.: {FailedResistanceDistance} | " +
        $"Direção: {FailedDirection} | Níveis inválidos: {FailedInvalidLevels} | R/R baixo: {FailedRiskReward} | Stop mín.: {FailedStopDistance} | " +
        $"Stop máx.: {FailedStopDistanceTooHigh} | RR teto: {FailedRiskRewardTooHigh} | Bull Trap: {FailedBullTrap} | " +
        $"Tendência (EMA): {FailedTrendConfirmation} | Momentum: {FailedMomentumFilter} | Short lateral: {FailedShortSideways} | Score Short máx.: {FailedShortScoreCeiling} | ADX Short BEAR máx.: {FailedShortAdxInBear} | Regime MeanRev: {FailedMeanReversionRegimeFilter} | ATR MeanRev: {FailedMeanReversionAtrFilter} | " +
        $"Duplicado hoje: {SkippedDuplicateToday} | Entrada rejeitada: {EntryRejected} ({EntryRejectionSummary}) | " +
        $"Passaram: {PassedAll}/{TotalAnalyzed}";
}
