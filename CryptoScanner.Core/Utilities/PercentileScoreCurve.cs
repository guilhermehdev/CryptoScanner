namespace CryptoScanner.Core.Utilities;

public sealed class PercentileScoreCurve
{
    private readonly List<(decimal Percentile, decimal Score)> _anchors;

    public PercentileScoreCurve(List<(decimal Percentile, decimal Score)> anchors)
    {
        _anchors = anchors.OrderBy(a => a.Percentile).ToList();
    }

    public decimal Evaluate(decimal input)
    {
        if (input <= _anchors[0].Percentile) return _anchors[0].Score;
        if (input >= _anchors[^1].Percentile) return _anchors[^1].Score;

        for (int i = 0; i < _anchors.Count - 1; i++)
        {
            var (p1, s1) = _anchors[i];
            var (p2, s2) = _anchors[i + 1];

            if (input >= p1 && input <= p2)
            {
                decimal ratio = p2 != p1 ? (input - p1) / (p2 - p1) : 0;
                return s1 + (s2 - s1) * ratio;
            }
        }

        return 0;
    }
}