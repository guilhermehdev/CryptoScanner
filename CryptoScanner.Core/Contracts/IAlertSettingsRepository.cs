using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface IAlertSettingsRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AlertSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AlertSettings settings, CancellationToken cancellationToken = default);
}