namespace CryptoScanner.Core.Models;

public sealed class StrategyDiagnostics
{
    public static string Label(string key) => key switch
    {
        "Breakout" => "Rompimento", "Pullback" => "Repique", "Legacy" => "Legado",
        "FailedScore" => "Score", "FailedConsolidation" => "Consolidação",
        "FailedVolumeSpike" => "Volume", "FailedResistanceDistance" => "Distância ao alvo",
        "FailedDirection" => "Tendência", "FailedRiskReward" => "R/R baixo",
        "FailedInvalidLevels" => "Níveis inválidos", "FailedStopDistance" => "Stop próximo",
        "FailedStopDistanceTooHigh" => "Stop distante", "FailedRiskRewardTooHigh" => "R/R acima do teto",
        "FailedBullTrap" => "Armadilha", "FailedMomentumFilter" => "Momentum", "FailedShortSideways" => "Short lateral", "FailedShortScoreCeiling" => "Score Short máx.", "FailedShortAdxInBear" => "ADX Short BEAR máx.",
        "FailedTrendConfirmation" => "Tendência (EMA)", "FailedMeanReversionRegimeFilter" => "Regime MeanRev", "FailedMeanReversionAtrFilter" => "ATR MeanRev", _ => key
    };
    public int Evaluated { get; set; }
    public int Triggered { get; set; }
    public int Eligible { get; set; }
    public Dictionary<string,int> Rejections { get; set; } = new();
    public Dictionary<string,int> SoleBlocker { get; set; } = new();
    public Dictionary<string,int> TargetPositions { get; set; } = new();
    public List<RejectedCandidate> Samples { get; set; } = new();
    public void Merge(StrategyDiagnostics other)
    {
        Evaluated += other.Evaluated; Triggered += other.Triggered; Eligible += other.Eligible;
        foreach(var pair in other.Rejections) Rejections[pair.Key]=Rejections.GetValueOrDefault(pair.Key)+pair.Value;
        foreach(var pair in other.SoleBlocker) SoleBlocker[pair.Key]=SoleBlocker.GetValueOrDefault(pair.Key)+pair.Value;
        foreach(var pair in other.TargetPositions) TargetPositions[pair.Key]=TargetPositions.GetValueOrDefault(pair.Key)+pair.Value;
        Samples=Samples.Concat(other.Samples).OrderBy(x=>x.AtUtc).ThenBy(x=>x.Symbol).Take(30).ToList();
    }
}
public sealed record RejectedCandidate(string Symbol, DateTime AtUtc, decimal Entry, decimal Stop, decimal Target, decimal RiskReward, string[] Failures)
{
    public CryptoScanner.Core.Models.Analysis.ResistanceZone? TargetZone { get; init; }
    public string TargetPosition => TargetZone?.PositionOf(Entry) ?? "Sem zona registrada";
}
