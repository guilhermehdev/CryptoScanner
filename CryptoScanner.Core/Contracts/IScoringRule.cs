using CryptoScanner.Core.Models.Analysis;

namespace CryptoScanner.Core.Contracts;

public interface IScoringRule
{
    string Name { get; }
    decimal Evaluate(ScoringContext context);
}