namespace CryptoScanner.Core.Configuration;

// Fase 1 do lado de venda: direção escolhida explicitamente antes de rodar o Backtest,
// não decidida automaticamente candle a candle. Long reproduz o comportamento de sempre;
// Short é o novo caminho, testável isoladamente antes de qualquer promoção pro app ao vivo.
public enum TradeDirection
{
    Long,
    Short
}