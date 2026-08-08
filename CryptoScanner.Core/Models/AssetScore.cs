namespace CryptoScanner.Core.Models;

public sealed class AssetScore : ObservableModel
{
    public string Symbol { get; init; } = "";

    private decimal _close;
    public decimal Close
    {
        get => _close;
        set
        {
            if (SetField(ref _close, value))
                OnPropertyChanged(nameof(CloseFormatted));
        }
    }

    public decimal Score { get; init; }
    public decimal OpportunityScore { get; init; }
    public decimal PreviousScore { get; init; }
    public decimal ScoreVariation { get; init; }
    public decimal Resistance { get; init; }
    public decimal Support { get; init; }
    public decimal? TakeProfit1 { get; init; }
    public decimal? TakeProfit3 { get; init; }
    public decimal VolumeSpike { get; init; }
    public decimal ResistanceDistance { get; init; }
    public decimal SupportDistance { get; init; }
    public decimal RiskReward { get; init; }
    public string TrendDirection { get; init; } = "";
    public bool IsBreakout { get; init; }
    public bool IsShortTermBreakout { get; init; }
    public decimal RelativeStrength { get; init; }
    public bool IsConsolidating { get; init; }
    public bool IsEliteSetup { get; init; }
    public bool HasExhaustion { get; init; }
    public string PatternName { get; init; } = "";
    public string BreakoutSource { get; init; } = "";
    public string MarketRegime { get; init; } = "";
    public decimal Rsi { get; init; }
    public decimal Adx { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal EmaDistanceAtr { get; init; }
    public decimal SwingUsageAtr { get; init; }
    public decimal VolumeImbalance { get; init; }
    public int TrendScore { get; init; }
    public int StructureScore { get; init; }
    public int VolumeScore { get; init; }
    public int CandleScore { get; init; }
    public int SetupScore { get; init; }
    public int MomentumScore { get; init; }
    public int VolatilityScore { get; init; }
    public int TrendStrengthScore { get; init; }
    public string SmartMoneyLabel { get; init; } = "";
    public bool IsBullTrap { get; init; }
    public bool IsBearTrap { get; init; }
    public bool IsEligible { get; init; }
    public bool IsFavorite { get; set; }
    public string CloseFormatted => Close >= 1 ? Close.ToString("N2") : Close.ToString("N8");
    public string Signal => OpportunityScore >= 70 ? "COMPRA+" :
                        OpportunityScore >= 55 ? "COMPRA" :
                        OpportunityScore >= 40 ? "MONITORAR" : "IGNORAR";
    public string EliteText => IsEliteSetup ? "⭐" : "";

    public string VariationText =>
        ScoreVariation > 0 ? $"▲ {ScoreVariation:F2}" :
        ScoreVariation < 0 ? $"▼ {Math.Abs(ScoreVariation):F2}" :
        "— 0.00";

    public string RelativeStrengthText =>
        RelativeStrength >= 0 ? $"+{RelativeStrength:F2}% vs BTC" : $"{RelativeStrength:F2}% vs BTC";

    public bool IsConsolidationRelevant => MarketRegime == "BULL";

    public string PartialExitTargetsText
    {
        get
        {
            if (TakeProfit1 == null && TakeProfit3 == null)
                return ""; // modo de risco sem saída parcial

            string tp1 = TakeProfit1.HasValue ? TakeProfit1.Value.ToString("0.########") : "—";
            string tp2 = Resistance.ToString("0.########");
            string tp3 = TakeProfit3.HasValue ? TakeProfit3.Value.ToString("0.########") : "—";

            return $"TP1 {tp1} | TP2 {tp2} | TP3 {tp3}";
        }
    }

    public string QualityAnalysis
    {
        get
        {
            if (IsBullTrap)
                return "🚫 Bull Trap — Não opere\n\n" +
                       "O Smart Money detectou uma armadilha de alta: o preço rompeu uma resistência, atraiu " +
                       "compradores, e reverteu logo em seguida — padrão clássico de \"puxada\" pra vender em cima " +
                       "de quem entrou atrasado. É o único sinal que a estratégia trata como desqualificante por " +
                       "si só, mesmo que o resto dos números pareça bom.";

            if (!IsEligible)
                return BuildIneligibilityReasons();

            var observations = new List<string>();

            // Volume
            if (VolumeSpike >= 1.8m)
                observations.Add($"Volume forte ({VolumeSpike:F2}× a média) — boa convicção por trás do movimento.");
            else if (VolumeSpike >= 1.5m)
                observations.Add($"Volume razoável ({VolumeSpike:F2}× a média) — participação real, mas não excepcional.");
            else if (VolumeSpike >= 1.30m)
                observations.Add($"Volume só raspando o mínimo exigido ({VolumeSpike:F2}× a média) — convicção fraca por trás do movimento.");
            else
                observations.Add($"Volume abaixo do piso normal ({VolumeSpike:F2}× a média).");

            // Resistência
            if (ResistanceDistance >= 10m)
                observations.Add($"Bastante espaço até a resistência ({ResistanceDistance:F1}%) — dá fôlego pro preço rodar antes de esbarrar num teto.");
            else if (ResistanceDistance >= 6m)
                observations.Add($"Espaço razoável até a resistência ({ResistanceDistance:F1}%).");
            else
                observations.Add($"Resistência próxima ({ResistanceDistance:F1}%) — pouco espaço pra rodar antes de esbarrar no teto, mesmo passando no piso mínimo.");

            // Stop / suporte
            if (SupportDistance >= 15m)
                observations.Add($"Stop bem distante ({SupportDistance:F1}%) — dentro do teto aceito, mas é uma posição de risco maior por operação.");
            else if (SupportDistance <= 2m)
                observations.Add($"Stop bem colado no preço ({SupportDistance:F1}%) — risco pequeno por operação, mas mais fácil de ser varrido por ruído normal do mercado.");
            else
                observations.Add($"Distância de stop equilibrada ({SupportDistance:F1}%).");

            // Risk/Reward
            if (RiskReward >= 3m)
                observations.Add($"Risk/Reward generoso ({RiskReward:F2}) — o alvo compensa bem o risco assumido.");
            else if (RiskReward < 2m)
                observations.Add($"Risk/Reward próximo do piso validado ({RiskReward:F2}) — margem mais apertada que o ideal.");

            // Força relativa
            if (RelativeStrength > 2m)
                observations.Add($"Performando bem melhor que o BTC ({RelativeStrengthText}) — força própria, não só surfando o mercado.");
            else if (RelativeStrength < 0)
                observations.Add($"Performando pior que o BTC ({RelativeStrengthText}) — a subida pode estar mais ligada ao mercado geral do que à moeda em si.");

            // Exaustão
            if (HasExhaustion)
                observations.Add("Sinal de exaustão de volume detectado — o movimento pode estar perdendo fôlego, não ganhando.");

            // Padrão de candle
            if (!string.IsNullOrEmpty(PatternName) && PatternName != "Doji")
                observations.Add($"Candle de força: {PatternName}.");
            else if (PatternName == "Doji")
                observations.Add("Candle de indecisão (Doji) — sem confirmação forte de direção nesse candle específico.");

            // Smart Money
            if (!string.IsNullOrEmpty(SmartMoneyLabel) && SmartMoneyLabel.Contains("Stop Hunt"))
                observations.Add($"{SmartMoneyLabel} — sugere que vendedores foram varridos antes do movimento, leitura construtiva.");

            string category = IsEliteSetup
                ? "⭐ Sinal Forte (Elite)"
                : (RelativeStrength < 0 || VolumeSpike < 1.5m || HasExhaustion || RiskReward < 2.0m)
                    ? "⚠ Aceitável — com ressalvas"
                    : "✅ Sinal Bom";

            return $"{category}\n\n" + string.Join(" ", observations);
        }
    }

    // ATENÇÃO: esses valores espelham a configuração validada em ScannerService.cs
    // (ValidatedThresholds). Se um dia mudarmos algum limiar lá (novo teto de RR, novo
    // piso de volume, etc.), essa reconstrução precisa ser atualizada junto — senão a
    // análise passa a mostrar um motivo de rejeição desatualizado.
    private string BuildIneligibilityReasons()
    {
        bool defensiveMode = MarketRegime != "BULL";

        decimal opportunity = MarketRegime switch
        {
            "BEAR" => OpportunityScore - 10m,   // ScannerSettings.BearRegimePenalty
            "LATERAL" => OpportunityScore - 8m, // ScannerSettings.SidewaysRegimePenalty
            _ => OpportunityScore
        };

        var reasons = new List<string>();

        if (opportunity < 60m)
            reasons.Add($"Score efetivo abaixo do mínimo ({opportunity:F1} < 60{(defensiveMode ? ", já descontada a penalidade de regime" : "")}).");

        bool passesBreakoutPath = defensiveMode
            ? (IsBreakout || IsShortTermBreakout || RelativeStrength >= 0m)
            : IsBreakout;
        if (!passesBreakoutPath)
        {
            reasons.Add(defensiveMode
                ? "Sem rompimento — nenhum dos 3 caminhos do modo defensivo foi atendido (clássico, curto prazo, ou força relativa)."
                : "Sem rompimento clássico da resistência.");
        }

        if (!defensiveMode && !IsConsolidating)
            reasons.Add("Sem consolidação prévia — exigida no regime BULL.");

        decimal volumeFloor = defensiveMode ? 1.10m : 1.30m;
        if (VolumeSpike < volumeFloor)
            reasons.Add($"Volume Spike abaixo do piso ({VolumeSpike:F2} < {volumeFloor:F2}).");

        if (ResistanceDistance < 4m)
            reasons.Add($"Resistência próxima demais ({ResistanceDistance:F1}% < 4% mínimo).");

        if (TrendDirection != "ALTA")
            reasons.Add($"Tendência não está em ALTA (atual: {TrendDirection}).");

        if (RiskReward < 1.5m)
            reasons.Add($"Risk/Reward abaixo do mínimo ({RiskReward:F2} < 1,5).");

        if (SupportDistance > 25m)
            reasons.Add($"Stop distante demais ({SupportDistance:F1}% > 25% máximo).");

        return reasons.Count == 0
            ? "Não foi possível identificar o motivo exato (verifique se algum limiar mudou no app ao vivo)."
            : string.Join(" ", reasons);
    }

    public string DisplaySignal
    {
        get
        {
            if (Signal == "IGNORAR")
                return "IGNORAR";

            return IsEligible ? Signal : "MONITORAR";
        }
    }
}