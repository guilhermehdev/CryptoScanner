using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public static class AlertChannelFactory
{
    public static List<IAlertChannel> BuildEnabledChannels(AlertSettings settings)
    {
        var channels = new List<IAlertChannel>();

        if (settings.TelegramEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken) && !string.IsNullOrWhiteSpace(settings.TelegramChatId))
            channels.Add(new TelegramAlertChannel(settings.TelegramBotToken, settings.TelegramChatId));

        if (settings.DiscordEnabled && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
            channels.Add(new DiscordAlertChannel(settings.DiscordWebhookUrl));

        if (settings.EmailEnabled && !string.IsNullOrWhiteSpace(settings.EmailSmtpHost) && !string.IsNullOrWhiteSpace(settings.EmailTo))
            channels.Add(new EmailAlertChannel(settings.EmailSmtpHost, settings.EmailSmtpPort, settings.EmailUsername, settings.EmailPassword, settings.EmailFrom, settings.EmailTo, settings.EmailUseSsl));

        return channels;
    }
}