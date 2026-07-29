namespace CryptoScanner.Application.Services;

public sealed class AlertDispatcher
{
    private readonly IReadOnlyList<IAlertChannel> _channels;

    public AlertDispatcher(IReadOnlyList<IAlertChannel> channels) => _channels = channels;

    public async Task<List<(string Channel, bool Success, string? Error)>> SendAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, bool, string?)>();

        foreach (var channel in _channels)
        {
            try
            {
                await channel.SendAsync(title, message, cancellationToken);
                results.Add((channel.Name, true, null));
            }
            catch (Exception ex)
            {
                results.Add((channel.Name, false, ex.Message));
            }
        }

        return results;
    }
}