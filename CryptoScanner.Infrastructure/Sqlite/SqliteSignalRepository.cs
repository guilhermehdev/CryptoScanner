using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteSignalRepository : ISignalRepository
{
    private readonly string _connectionString;

    public SqliteSignalRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS Signals
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                Price REAL NOT NULL,
                FinalScore REAL NOT NULL,
                Signal TEXT NOT NULL,
                OutcomePrice REAL,
                OutcomePercent REAL,
                PreviousScore REAL,
                Evaluated INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Signals_Evaluated_Timestamp ON Signals (Evaluated, Timestamp);
            CREATE INDEX IF NOT EXISTS IX_Signals_Symbol_Timestamp ON Signals (Symbol, Timestamp);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertSignalAsync(string symbol, decimal price, decimal score, string signal, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync("""
            INSERT INTO Signals (Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, Evaluated)
            VALUES (@Timestamp, @Symbol, @Price, @Score, @Signal, NULL, NULL, 0)
            """, cancellationToken,
            ("@Timestamp", DateTime.UtcNow.ToString("O")), ("@Symbol", symbol), ("@Price", (double)price), ("@Score", (double)score), ("@Signal", signal));
    }

    public async Task<bool> SignalExistsTodayAsync(string symbol, string signal, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM Signals WHERE Symbol = @Symbol AND Signal = @Signal AND Timestamp >= @StartOfDay";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@Signal", signal);
        command.Parameters.AddWithValue("@StartOfDay", DateTime.UtcNow.Date.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public Task<IReadOnlyList<SignalHistory>> GetSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync("SELECT Id, Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, Evaluated FROM Signals ORDER BY Id DESC", cancellationToken);

    public Task<IReadOnlyList<SignalHistory>> GetPendingSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync("SELECT Id, Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, Evaluated FROM Signals WHERE Evaluated = 0", cancellationToken);

    public Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE Signals SET OutcomePrice = @OutcomePrice, OutcomePercent = @OutcomePercent, Evaluated = 1 WHERE Id = @Id", cancellationToken,
            ("@Id", id), ("@OutcomePrice", (double)outcomePrice), ("@OutcomePercent", (double)outcomePercent));

    public async Task<double> GetWinRateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*), COALESCE(SUM(CASE WHEN OutcomePercent > 0 THEN 1 ELSE 0 END), 0) FROM Signals WHERE Evaluated = 1";
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        long total = reader.GetInt64(0);
        return total == 0 ? 0 : reader.GetInt64(1) * 100d / total;
    }

    public async Task<double> GetAverageReturnAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT AVG(OutcomePercent) FROM Signals WHERE Evaluated = 1", connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToDouble(value);
    }

    private async Task<IReadOnlyList<SignalHistory>> ReadSignalsAsync(string sql, CancellationToken cancellationToken)
    {
        var signals = new List<SignalHistory>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            signals.Add(new SignalHistory
            {
                Id = reader.GetInt32(0), Timestamp = DateTime.Parse(reader.GetString(1)), Symbol = reader.GetString(2),
                Price = Convert.ToDecimal(reader.GetDouble(3)), FinalScore = Convert.ToDecimal(reader.GetDouble(4)), Signal = reader.GetString(5),
                OutcomePrice = reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetDouble(6)),
                OutcomePercent = reader.IsDBNull(7) ? null : Convert.ToDecimal(reader.GetDouble(7)), Evaluated = !reader.IsDBNull(8) && reader.GetInt32(8) == 1
            });
        }
        return signals;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
