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
    MeanReversionConfirmed,
    // V3: toque na banda inferior seguido de recuperação confirmada.
    BollingerLowerReclaim,
    // Hipótese independente para pesquisa: rompimento fechado e reteste confirmado
    // no candle seguinte. Disponível apenas no backtest Intraday.
    IntradayBreakoutRetest,
    // Estreitamento e abertura das Bandas de Bollinger. Disponível somente na pesquisa
    // do Backtest até a validação cronológica concluir.
    BollingerSqueezeBreakout,
    // Pullback na onda/EMA 34 com inclinação direcional, toque, candle de confirmação,
    // filtro de RSI e risco/retorno mínimo de 2:1. Executada diretamente pelo backtest.
    WavePullback
}
