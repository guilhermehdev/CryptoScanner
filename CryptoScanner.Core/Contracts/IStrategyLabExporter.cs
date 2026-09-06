namespace CryptoScanner.Core.Contracts;

public interface IStrategyLabExporter
{
    Task ExportAsync(Stream destination, CancellationToken token = default);
}
