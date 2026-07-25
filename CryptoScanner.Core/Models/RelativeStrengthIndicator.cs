using System.Collections.Generic;

using CryptoScanner.Core.Models;

namespace CryptoScanner.Indicators.Indicators
{
    public static class RelativeStrengthIndicator
    {
        /// <summary>
        /// Retorno percentual do ativo menos o retorno percentual do BTC no mesmo período.
        /// Positivo = ativo performou melhor que o BTC (força relativa real).
        /// Requer candles do mesmo timeframe (ex.: ambos 1h) para comparação justa.
        /// </summary>
        public static decimal Calculate(List<Candle> assetCandles, List<Candle> btcCandles, int period)
        {
            decimal assetReturn = ReturnPercent(assetCandles, period);
            decimal btcReturn = ReturnPercent(btcCandles, period);
            return assetReturn - btcReturn;
        }

        private static decimal ReturnPercent(List<Candle> candles, int period)
        {
            if (candles.Count < period + 1)
                return 0;

            decimal past = candles[^(period + 1)].Close;
            decimal current = candles[^1].Close;

            if (past == 0)
                return 0;

            return ((current - past) / past) * 100m;
        }
    }
}