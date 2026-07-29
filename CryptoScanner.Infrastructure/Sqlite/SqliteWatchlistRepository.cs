using CryptoScanner.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteWatchlistRepository : IWatchlistRepository
{
    private readonly string _connectionString;

    public SqliteWatchlistRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS Watchlist
            (
                Symbol TEXT PRIMARY KEY,
                AddedAt TEXT NOT NULL
            );
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT Symbol FROM Watchlist ORDER BY AddedAt DESC", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));

        return result;
    }

    public async Task AddAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(
            "INSERT OR IGNORE INTO Watchlist (Symbol, AddedAt) VALUES (@Symbol, @AddedAt)", connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@AddedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string symbol, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("DELETE FROM Watchlist WHERE Symbol = @Symbol", connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}