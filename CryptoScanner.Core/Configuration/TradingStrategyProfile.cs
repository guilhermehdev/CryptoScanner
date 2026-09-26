namespace CryptoScanner.Core.Configuration;

/// <summary>
/// Perfil de estratégia executável. Cada opção representa uma hipótese de entrada
/// independente e mantém os resultados separados no backtest.
/// </summary>
public enum TradingStrategyProfile
{
    BreakoutTrend,
    PullbackTrend,
    MeanReversion
}

public static class TradingStrategyProfiles
{
    public static EntryStrategy EntryStrategyFor(TradingStrategyProfile profile) => profile switch
    {
        TradingStrategyProfile.BreakoutTrend => EntryStrategy.Breakout,
        TradingStrategyProfile.PullbackTrend => EntryStrategy.Pullback,
        TradingStrategyProfile.MeanReversion => EntryStrategy.MeanReversion,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    public static string DisplayName(TradingStrategyProfile profile) => profile switch
    {
        TradingStrategyProfile.BreakoutTrend => "Breakout Trend",
        TradingStrategyProfile.PullbackTrend => "Pullback Trend",
        TradingStrategyProfile.MeanReversion => "Mean Reversion",
        _ => profile.ToString()
    };
}
