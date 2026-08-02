namespace CryptoScanner.Core.Configuration;

public enum RiskCalculationMode
{
    SwingBased,          // comportamento atual: máxima/mínima dos últimos 50 candles
    AtrBased,            // stop/alvo como múltiplo do ATR, RR fixo por construção
    SwingWithAtrBuffer   // suporte/resistência reais (Swing), mas o stop ganha uma folga extra em ATR
}