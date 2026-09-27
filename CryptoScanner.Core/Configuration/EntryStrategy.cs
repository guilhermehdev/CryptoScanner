namespace CryptoScanner.Core.Configuration;
public enum EntryStrategy
{
    Legacy,
    Breakout,
    Pullback,
    MeanReversion,
    Auto,
    // V2: entra somente depois de um candle fechado romper a máxima do candle de reação.
    // MeanReversion permanece para reproduzir os testes históricos da V1.
    MeanReversionConfirmed
}
