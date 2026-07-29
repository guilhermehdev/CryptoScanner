using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteAlertSettingsRepository : IAlertSettingsRepository
{
    private readonly string _connectionString;

    public SqliteAlertSettingsRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS AlertSettings
            (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                DesktopEnabled INTEGER DEFAULT 0,
                TelegramEnabled INTEGER DEFAULT 0,
                TelegramBotToken TEXT DEFAULT '',
                TelegramChatId TEXT DEFAULT '',
                DiscordEnabled INTEGER DEFAULT 0,
                DiscordWebhookUrl TEXT DEFAULT '',
                EmailEnabled INTEGER DEFAULT 0,
                EmailSmtpHost TEXT DEFAULT '',
                EmailSmtpPort INTEGER DEFAULT 587,
                EmailUsername TEXT DEFAULT '',
                EmailPassword TEXT DEFAULT '',
                EmailFrom TEXT DEFAULT '',
                EmailTo TEXT DEFAULT '',
                EmailUseSsl INTEGER DEFAULT 1
            );
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AlertSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT * FROM AlertSettings WHERE Id = 1", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return new AlertSettings();

        return new AlertSettings
        {
            DesktopEnabled = reader.GetInt32(reader.GetOrdinal("DesktopEnabled")) == 1,
            TelegramEnabled = reader.GetInt32(reader.GetOrdinal("TelegramEnabled")) == 1,
            TelegramBotToken = reader.GetString(reader.GetOrdinal("TelegramBotToken")),
            TelegramChatId = reader.GetString(reader.GetOrdinal("TelegramChatId")),
            DiscordEnabled = reader.GetInt32(reader.GetOrdinal("DiscordEnabled")) == 1,
            DiscordWebhookUrl = reader.GetString(reader.GetOrdinal("DiscordWebhookUrl")),
            EmailEnabled = reader.GetInt32(reader.GetOrdinal("EmailEnabled")) == 1,
            EmailSmtpHost = reader.GetString(reader.GetOrdinal("EmailSmtpHost")),
            EmailSmtpPort = reader.GetInt32(reader.GetOrdinal("EmailSmtpPort")),
            EmailUsername = reader.GetString(reader.GetOrdinal("EmailUsername")),
            EmailPassword = reader.GetString(reader.GetOrdinal("EmailPassword")),
            EmailFrom = reader.GetString(reader.GetOrdinal("EmailFrom")),
            EmailTo = reader.GetString(reader.GetOrdinal("EmailTo")),
            EmailUseSsl = reader.GetInt32(reader.GetOrdinal("EmailUseSsl")) == 1
        };
    }

    public async Task SaveAsync(AlertSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO AlertSettings
            (Id, DesktopEnabled, TelegramEnabled, TelegramBotToken, TelegramChatId,
             DiscordEnabled, DiscordWebhookUrl,
             EmailEnabled, EmailSmtpHost, EmailSmtpPort, EmailUsername, EmailPassword, EmailFrom, EmailTo, EmailUseSsl)
            VALUES
            (1, @DesktopEnabled, @TelegramEnabled, @TelegramBotToken, @TelegramChatId,
             @DiscordEnabled, @DiscordWebhookUrl,
             @EmailEnabled, @EmailSmtpHost, @EmailSmtpPort, @EmailUsername, @EmailPassword, @EmailFrom, @EmailTo, @EmailUseSsl)
            ON CONFLICT(Id) DO UPDATE SET
                DesktopEnabled = excluded.DesktopEnabled,
                TelegramEnabled = excluded.TelegramEnabled,
                TelegramBotToken = excluded.TelegramBotToken,
                TelegramChatId = excluded.TelegramChatId,
                DiscordEnabled = excluded.DiscordEnabled,
                DiscordWebhookUrl = excluded.DiscordWebhookUrl,
                EmailEnabled = excluded.EmailEnabled,
                EmailSmtpHost = excluded.EmailSmtpHost,
                EmailSmtpPort = excluded.EmailSmtpPort,
                EmailUsername = excluded.EmailUsername,
                EmailPassword = excluded.EmailPassword,
                EmailFrom = excluded.EmailFrom,
                EmailTo = excluded.EmailTo,
                EmailUseSsl = excluded.EmailUseSsl
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@DesktopEnabled", settings.DesktopEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@TelegramEnabled", settings.TelegramEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@TelegramBotToken", settings.TelegramBotToken);
        command.Parameters.AddWithValue("@TelegramChatId", settings.TelegramChatId);
        command.Parameters.AddWithValue("@DiscordEnabled", settings.DiscordEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@DiscordWebhookUrl", settings.DiscordWebhookUrl);
        command.Parameters.AddWithValue("@EmailEnabled", settings.EmailEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@EmailSmtpHost", settings.EmailSmtpHost);
        command.Parameters.AddWithValue("@EmailSmtpPort", settings.EmailSmtpPort);
        command.Parameters.AddWithValue("@EmailUsername", settings.EmailUsername);
        command.Parameters.AddWithValue("@EmailPassword", settings.EmailPassword);
        command.Parameters.AddWithValue("@EmailFrom", settings.EmailFrom);
        command.Parameters.AddWithValue("@EmailTo", settings.EmailTo);
        command.Parameters.AddWithValue("@EmailUseSsl", settings.EmailUseSsl ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}