namespace CryptoScanner.Core.Models;

public sealed class SimulatedTrade : ObservableModel
{
    public int Id { get; set; }
    public string Symbol { get; set; } = "";
    public DateTime EntryTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal StopLoss { get; set; }
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
                OnPropertyChanged(nameof(IsOpen)); // IsOpen depende de Closed, não notifica sozinho
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
}