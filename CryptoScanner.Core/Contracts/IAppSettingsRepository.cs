namespace CryptoScanner.Core.Contracts;

public interface IAppSettingsRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}