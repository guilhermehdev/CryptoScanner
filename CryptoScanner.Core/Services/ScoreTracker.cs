using System.Collections.Concurrent;

namespace CryptoScanner.Core.Services
{
    public static class ScoreTracker
    {
        private static readonly ConcurrentDictionary<string, decimal> _lastScores = new();

        /// <summary>
        /// Registers the current score for a symbol and returns the score from the
        /// previous scan along with the variation. On first sighting of a symbol,
        /// both previous score and variation equal the current score / zero.
        /// </summary>
        public static (decimal Previous, decimal Variation) Update(string symbol, decimal currentScore)
        {
            decimal previous = _lastScores.GetOrAdd(symbol, currentScore);
            decimal variation = currentScore - previous;
            _lastScores[symbol] = currentScore;
            return (previous, variation);
        }
    }
}