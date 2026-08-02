namespace CryptoScanner.Core.Contracts;

public interface IBacktestSettingsRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string?> GetManualSymbolListAsync(CancellationToken cancellationToken = default);
    Task SaveManualSymbolListAsync(string commaSeparatedSymbols, CancellationToken cancellationToken = default);
}