using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Services
{
    public static class ScoreTracker
    {
        private static readonly Dictionary<string, decimal> _lastScores = [];

        public static decimal GetVariation(string symbol, decimal currentScore)
        {
            if (!_lastScores.TryGetValue(symbol, out decimal previous))
            {
                _lastScores[symbol] = currentScore;
                return 0;
            }

            decimal variation = currentScore - previous;

            _lastScores[symbol] = currentScore;

            return variation;
        }
    }
}
