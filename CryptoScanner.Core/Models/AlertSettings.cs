namespace CryptoScanner.Core.Models;

public sealed class AlertSettings
{
    public bool DesktopEnabled { get; set; }

    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; } = "";
    public string TelegramChatId { get; set; } = "";

    public bool DiscordEnabled { get; set; }
    public string DiscordWebhookUrl { get; set; } = "";

    public bool EmailEnabled { get; set; }
    public string EmailSmtpHost { get; set; } = "";
    public int EmailSmtpPort { get; set; } = 587;
    public string EmailUsername { get; set; } = "";
    public string EmailPassword { get; set; } = "";
    public string EmailFrom { get; set; } = "";
    public string EmailTo { get; set; } = "";
    public bool EmailUseSsl { get; set; } = true;
}