namespace CryptoScanner.Core.Configuration;

/// <summary>
/// Perfil de estratégia executável. Cada opção representa uma hipótese de entrada
/// independente e mantém os resultados separados no backtest.
/// </summary>
public enum TradingStrategyProfile
{
    BreakoutTrend,
    PullbackTrend,
    MeanReversion,
    IntradayBreakoutRetest,
    BollingerSqueezeBreakout
}

public static class TradingStrategyProfiles
{
    public static EntryStrategy EntryStrategyFor(TradingStrategyProfile profile) => profile switch
    {
        TradingStrategyProfile.BreakoutTrend => EntryStrategy.Breakout,
        TradingStrategyProfile.PullbackTrend => EntryStrategy.Pullback,
        TradingStrategyProfile.MeanReversion => EntryStrategy.BollingerLowerReclaim,
        TradingStrategyProfile.IntradayBreakoutRetest => EntryStrategy.IntradayBreakoutRetest,
        TradingStrategyProfile.BollingerSqueezeBreakout => EntryStrategy.BollingerSqueezeBreakout,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    public static string DisplayName(TradingStrategyProfile profile) => profile switch
    {
        TradingStrategyProfile.BreakoutTrend => "Breakout Trend",
        TradingStrategyProfile.PullbackTrend => "Pullback Trend",
        TradingStrategyProfile.MeanReversion => "Reversão Bollinger Long (V3)",
        TradingStrategyProfile.IntradayBreakoutRetest => "Rompimento + Reteste Intraday (experimental)",
        TradingStrategyProfile.BollingerSqueezeBreakout => "EAB — Bollinger Squeeze (pesquisa)",
        _ => profile.ToString()
    };
}
