using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CryptoScanner.UI.Services;

public class SignalDatabase
{
    private readonly string _connectionString;

    public SignalDatabase()
    {
        string dbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "signals.db");

        _connectionString =
            $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql = """
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
                Evaluated INTEGER DEFAULT 0
            );
            """;

        await using var command =
            new SqliteCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertSignalAsync(
    string symbol,
    decimal price,
    decimal finalScore,
    string signal)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql = """
        INSERT INTO Signals
        (
            Timestamp,
            Symbol,
            Price,
            FinalScore,
            Signal
        )
        VALUES
        (
            @Timestamp,
            @Symbol,
            @Price,
            @FinalScore,
            @Signal
        );
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@Timestamp",
            DateTime.UtcNow.ToString("O"));

        command.Parameters.AddWithValue(
            "@Symbol",
            symbol);

        command.Parameters.AddWithValue(
            "@Price",
            (double)price);

        command.Parameters.AddWithValue(
            "@FinalScore",
            (double)finalScore);

        command.Parameters.AddWithValue(
            "@Signal",
            signal);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> SignalExistsTodayAsync(
    string symbol)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql = """
        SELECT COUNT(*)
        FROM Signals
        WHERE Symbol = @Symbol
        AND date(Timestamp) = date('now')
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@Symbol",
            symbol);

        long count =
            (long)(await command.ExecuteScalarAsync() ?? 0);

        return count > 0;
    }

    public async Task<List<SignalHistory>> GetSignalsAsync()
    {
        List<SignalHistory> result = new();

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql =
            """
        SELECT
            Timestamp,
            Symbol,
            Price,
            FinalScore,
            Signal
        FROM Signals
        ORDER BY Id DESC
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(
                new SignalHistory
                {
                    Timestamp =
                        DateTime.Parse(
                            reader.GetString(0)),

                    Symbol =
                        reader.GetString(1),

                    Price =
                        Convert.ToDecimal(
                            reader.GetDouble(2)),

                    FinalScore =
                        Convert.ToDecimal(
                            reader.GetDouble(3)),

                    Signal =
                        reader.GetString(4)
                });
        }

        return result;
    }

    public async Task<List<SignalHistory>> GetPendingSignalsAsync()
    {
        List<SignalHistory> result = new();

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql =
            """
        SELECT
            Timestamp,
            Symbol,
            Price,
            FinalScore,
            Signal
        FROM Signals
        WHERE Evaluated = 0
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(
                new SignalHistory
                {
                    Timestamp =
                        DateTime.Parse(reader.GetString(0)),
                    Symbol =
                        reader.GetString(1),
                    Price =
                        Convert.ToDecimal(reader.GetDouble(2)),
                    FinalScore =
                        Convert.ToDecimal(reader.GetDouble(3)),
                    Signal =
                        reader.GetString(4)
                });
        }

        return result;
    }

    public async Task UpdateSignalResultAsync(
    string symbol,
    DateTime timestamp,
    decimal outcomePrice,
    decimal outcomePercent)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql =
            """
        UPDATE Signals
        SET
            OutcomePrice = @OutcomePrice,
            OutcomePercent = @OutcomePercent,
            Evaluated = 1
        WHERE
            Symbol = @Symbol
            AND Timestamp = @Timestamp
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@OutcomePrice",
            (double)outcomePrice);

        command.Parameters.AddWithValue(
            "@OutcomePercent",
            (double)outcomePercent);

        command.Parameters.AddWithValue(
            "@Symbol",
            symbol);

        command.Parameters.AddWithValue(
            "@Timestamp",
            timestamp.ToString("O"));

        await command.ExecuteNonQueryAsync();
    }
}