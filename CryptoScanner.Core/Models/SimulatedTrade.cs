namespace CryptoScanner.Core.Models;

public sealed class SimulatedTrade : ObservableModel
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal TakeProfit { get; set; }
    private decimal _stopLoss;
    public decimal StopLoss
    {
        get => _stopLoss;
        set => SetField(ref _stopLoss, value);
    }
    public string Note { get; set; } = "";
    public string Profile { get; set; } = "";

    // Raio-x completo do momento da entrada — inalterado, sem mudança
    public decimal ScoreAtEntry { get; set; }
    public decimal Rsi { get; set; }
    public decimal Adx { get; set; }
    public decimal AtrPercent { get; set; }
    public decimal EmaDistanceAtr { get; set; }
    public decimal SwingUsageAtr { get; set; }
    public decimal VolumeSpike { get; set; }
    public decimal VolumeImbalance { get; set; }
    public decimal RelativeStrength { get; set; }
    public decimal RiskRewardAtEntry { get; set; }
    public decimal TrendScore { get; set; }
    public decimal StructureScore { get; set; }
    public decimal VolumeScore { get; set; }
    public decimal CandleScore { get; set; }
    public decimal SetupScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal VolatilityScore { get; set; }
    public decimal TrendStrengthScore { get; set; }
    public string PatternName { get; set; } = "";
    public string SmartMoneyLabel { get; set; } = "";
    public string BreakoutSource { get; set; } = "";
    public string MarketRegime { get; set; } = "";
    public bool IsBullTrap { get; set; }
    public bool IsBearTrap { get; set; }

    // A partir daqui, os campos que podem mudar depois de criado o trade — precisam
    // notificar a tela quando o preço em tempo real (WebSocket) fecha o trade sozinho,
    // sem esperar o próximo scan recarregar o grid inteiro.

    private bool _closed;
    public bool Closed
    {
        get => _closed;
        set
        {
            if (SetField(ref _closed, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(PartialExitProgressText));
            }
        }
    }

    private DateTime? _exitTime;
    public DateTime? ExitTime
    {
        get => _exitTime;
        set => SetField(ref _exitTime, value);
    }

    private decimal? _exitPrice;
    public decimal? ExitPrice
    {
        get => _exitPrice;
        set => SetField(ref _exitPrice, value);
    }

    private decimal? _outcomePercent;
    public decimal? OutcomePercent
    {
        get => _outcomePercent;
        set => SetField(ref _outcomePercent, value);
    }

    private string _exitReason = "";
    public string ExitReason
    {
        get => _exitReason;
        set => SetField(ref _exitReason, value);
    }

    public bool IsOpen => !Closed;

    private decimal? _currentPrice;
    public decimal? CurrentPrice
    {
        get => _currentPrice;
        set => SetField(ref _currentPrice, value);
    }

    private decimal? _unrealizedPnLPercent;
    public decimal? UnrealizedPnLPercent
    {
        get => _unrealizedPnLPercent;
        set => SetField(ref _unrealizedPnLPercent, value);
    }

    // Estado da saída parcial (TP1→TP2→breakeven→TP3). TakeProfit já existente
    // continua sendo o TP2 (resistência estrutural); TakeProfit1/3 são os alvos extras.
    public decimal? TakeProfit1 { get; set; }
    public decimal? TakeProfit3 { get; set; }

    private bool _tp1Hit;
    public bool Tp1Hit
    {
        get => _tp1Hit;
        set
        {
            if (SetField(ref _tp1Hit, value))
                OnPropertyChanged(nameof(PartialExitProgressText));
        }
    }

    private bool _tp2Hit;
    public bool Tp2Hit
    {
        get => _tp2Hit;
        set
        {
            if (SetField(ref _tp2Hit, value))
                OnPropertyChanged(nameof(PartialExitProgressText));
        }
    }



    private decimal _remainingFraction = 1.0m;
    public decimal RemainingFraction
    {
        get => _remainingFraction;
        set => SetField(ref _remainingFraction, value);
    }

    private decimal _weightedExitSum;
    public decimal WeightedExitSum
    {
        get => _weightedExitSum;
        set => SetField(ref _weightedExitSum, value);
    }

    public string PartialExitProgressText
    {
        get
        {
            if (TakeProfit1 == null)
                return ""; // trade sem saída parcial (modo antigo, ou criado antes da 3.2)

            string tp1Check = Tp1Hit ? "✓" : "—";
            string tp2Check = Tp2Hit ? "✓" : "—";
            string tp3Check = Closed && ExitReason == "TP1TP2TP3" ? "✓" : "—";

            // Valor de cada alvo ao lado do check — antes só mostrava se bateu ou não,
            // sem dizer o preço. TakeProfit (já existente) é o TP2; TakeProfit1/3 são
            // os alvos extras da saída parcial.
            string tp1Value = TakeProfit1.Value.ToString("0.########");
            string tp2Value = TakeProfit.ToString("0.########");
            string tp3Value = TakeProfit3?.ToString("0.########") ?? "—";

            return $"TP1 {tp1Check} {tp1Value} | TP2 {tp2Check} {tp2Value} | TP3 {tp3Check} {tp3Value}";
        }
    }


}