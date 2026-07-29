using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CryptoScanner.Application.Services;

public sealed class DiscordAlertChannel : IAlertChannel
{
    private readonly string _webhookUrl;
    private static readonly HttpClient Http = new();

    public string Name => "Discord";

    public DiscordAlertChannel(string webhookUrl) => _webhookUrl = webhookUrl;

    public async Task SendAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var payload = new { content = $"**{title}**\n{message}" };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(_webhookUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}