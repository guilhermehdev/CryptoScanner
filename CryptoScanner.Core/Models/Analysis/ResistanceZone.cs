namespace CryptoScanner.Core.Models.Analysis;

public sealed class ResistanceZone
{
    public required decimal Price { get; init; }
    public required int TouchCount { get; init; }
    public required bool HasStrongRejection { get; init; }
    public required bool HasVolumeConfirmation { get; init; }
    public required bool IsRecent { get; init; }
    public required decimal Score { get; init; }
    public required DateTime LastTestTime { get; init; }
}