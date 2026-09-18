namespace CryptoScanner.Core.Models;

public sealed record LlmOpinionValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public string Message => IsValid ? "Válida" : string.Join(" ", Errors);
}
