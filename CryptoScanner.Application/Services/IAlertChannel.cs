namespace CryptoScanner.Application.Services;

public interface IAlertChannel
{
    string Name { get; }
    Task SendAsync(string title, string message, CancellationToken cancellationToken = default);
}