using CryptoScanner.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteBacktestSettingsRepository : IBacktestSettingsRepository
{
    private readonly string _connectionString;
    private const string ManualSymbolListKey = "ManualSymbolList";

    public SqliteBacktestSettingsRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS BacktestSettings
            (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetManualSymbolListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT Value FROM BacktestSettings WHERE Key = @Key", connection);
        command.Parameters.AddWithValue("@Key", ManualSymbolListKey);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public async Task SaveManualSymbolListAsync(string commaSeparatedSymbols, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO BacktestSettings (Key, Value) VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Key", ManualSymbolListKey);
        command.Parameters.AddWithValue("@Value", commaSeparatedSymbols);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}