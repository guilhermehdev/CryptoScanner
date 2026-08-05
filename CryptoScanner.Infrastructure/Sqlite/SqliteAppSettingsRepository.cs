using CryptoScanner.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteAppSettingsRepository : IAppSettingsRepository
{
    private readonly string _connectionString;

    public SqliteAppSettingsRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS AppSettings
            (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT Value FROM AppSettings WHERE Key = @Key", connection);
        command.Parameters.AddWithValue("@Key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO AppSettings (Key, Value) VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value = @Value
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Key", key);
        command.Parameters.AddWithValue("@Value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}