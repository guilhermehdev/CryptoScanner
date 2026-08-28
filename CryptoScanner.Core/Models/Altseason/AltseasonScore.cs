namespace CryptoScanner.Core.Models.Altseason;

public sealed record AltseasonIndicatorScore
{
    public required string Name { get; init; }
    public decimal Score { get; init; }
    public decimal Weight { get; init; }
    public decimal Contribution => Score * Weight;
    public string Signal => Score >= 75m ? "BULLISH" : Score >= 50m ? "NEUTRAL+" : Score >= 35m ? "NEUTRAL" : "BEARISH";
}

public sealed record AltseasonScore
{
    public decimal MarketRegimeScore { get; init; }
    public decimal Score { get; init; }
    public decimal PreviousScore { get; init; }
    public decimal Change => Score - PreviousScore;
    public IReadOnlyList<AltseasonIndicatorScore> Indicators { get; init; } = Array.Empty<AltseasonIndicatorScore>();

    public string State => Score >= 85m ? "ALTSEASON" :
                           Score >= 75m ? "RISK ON" :
                           Score >= 65m ? "CONFIRMAÇÃO" :
                           Score >= 50m ? "ACUMULAÇÃO" :
                           Score >= 35m ? "NEUTRO" : "RISK OFF";

    public string Action => Score >= 75m ? "AUMENTAR" :
                            Score >= 50m ? "ACUMULAR" :
                            Score >= 35m ? "SELETIVO" : "REDUZIR RISCO";
}
