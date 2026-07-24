namespace CryptoScanner.Core.Models.Analysis;

public sealed class StructureAnalysis
{
    public int Score { get; init; }
    public bool IsUptrend { get; init; }
    public bool IsDowntrend { get; init; }
    public bool IsStrongUptrend { get; init; }
    public bool IsStrongDowntrend { get; init; }
    public bool HasBreakOfStructure { get; init; }
    public bool HasChangeOfCharacter { get; init; }
}
