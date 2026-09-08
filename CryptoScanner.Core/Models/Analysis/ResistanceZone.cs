namespace CryptoScanner.Core.Models.Analysis;

public sealed class ResistanceZone
{
    public required decimal Price { get; init; }
    // Observed pivot-high range, not an assumed ATR band. Old records fall back to Price.
    public decimal? LowerBound { get; init; }
    public decimal? UpperBound { get; init; }
    public decimal Lower => LowerBound ?? Price;
    public decimal Upper => UpperBound ?? Price;
    public string PositionOf(decimal price) => price < Lower ? "Abaixo da zona" : price > Upper ? "Acima da zona" : "Dentro da zona";
    public required int TouchCount { get; init; }
    public required bool HasStrongRejection { get; init; }
    public required bool HasVolumeConfirmation { get; init; }
    public required bool IsRecent { get; init; }
    public required decimal Score { get; init; }
    public required DateTime LastTestTime { get; init; }
}
