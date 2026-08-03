using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Application.Services;

public sealed class ScoringEngine
{
    private readonly List<IScoringRule> _rules;
    public ScoringEngine(List<IScoringRule> rules) => _rules = rules;

    public (decimal TotalScore, Dictionary<string, decimal> Breakdown) Evaluate(ScoringContext context)
    {
        var breakdown = new Dictionary<string, decimal>();
        decimal total = 0;

        foreach (var rule in _rules)
        {
            decimal points = rule.Evaluate(context);
            breakdown[rule.Name] = points;
            total += points;
        }

        return (total, breakdown);
    }
}