using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CryptoScanner.Application.Services;

public sealed class TelegramAlertChannel : IAlertChannel
{
    private readonly string _botToken;
    private readonly string _chatId;
    private static readonly HttpClient Http = new();

    public string Name => "Telegram";

    public TelegramAlertChannel(string botToken, string chatId)
    {
        _botToken = botToken;
        _chatId = chatId;
    }

    public async Task SendAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
        string text = $"*{title}*\n{message}";

        var payload = new { chat_id = _chatId, text, parse_mode = "Markdown" };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}