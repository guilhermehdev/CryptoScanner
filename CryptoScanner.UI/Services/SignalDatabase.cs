using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

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
            Signal,
            OutcomePrice,
            OutcomePercent,
            Evaluated
        )
        VALUES
        (
            @Timestamp,
            @Symbol,
            @Price,
            @FinalScore,
            @Signal,
            NULL,
            NULL,
            0
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
    string symbol,
    string signal)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql = """
        SELECT COUNT(*)
        FROM Signals
        WHERE Symbol = @Symbol
        AND Signal = @Signal
        AND date(Timestamp) = date('now')
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        command.Parameters.AddWithValue(
            "@Symbol",
            symbol);
        command.Parameters.AddWithValue(
            "@Signal",
            signal);    

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
            Timestamp,Symbol,Price,FinalScore,Signal,OutcomePrice,OutcomePercent,Evaluated FROM Signals
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
                        reader.GetString(4),

                    OutcomePrice =
                        reader.IsDBNull(5)
                            ? null
                            : Convert.ToDecimal(
                                reader.GetDouble(5)),

                    OutcomePercent =
                        reader.IsDBNull(6)
                            ? null
                            : Convert.ToDecimal(
                                reader.GetDouble(6)),

                    Evaluated =
                        !reader.IsDBNull(7)
                        && reader.GetInt32(7) == 1
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
        SELECT id,Timestamp,Symbol,Price,FinalScore,Signal,OutcomePrice,OutcomePercent,Evaluated FROM Signals WHERE Evaluated = 0
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new SignalHistory {
                Id = reader.GetInt32(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                Symbol = reader.GetString(2),
                Price = Convert.ToDecimal(reader.GetDouble(3)),
                FinalScore = Convert.ToDecimal(reader.GetDouble(4)),
                Signal = reader.GetString(5),
                OutcomePrice = reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetDouble(6)),
                OutcomePercent = reader.IsDBNull(7) ? null : Convert.ToDecimal(reader.GetDouble(7)),
                Evaluated = !reader.IsDBNull(8) && reader.GetInt32(8) == 1
            });
        }

        return result;
    }

    public async Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent)
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
        WHERE Id = @Id
        """;

        await using var command =
            new SqliteCommand(sql, connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@OutcomePrice", (double)outcomePrice);
        command.Parameters.AddWithValue("@OutcomePercent", (double)outcomePercent);

        //await command.ExecuteNonQueryAsync();
        int rows =
    await command.ExecuteNonQueryAsync();

        MessageBox.Show(
            $"Linhas atualizadas: {rows}");

    }

    public async Task<double> GetWinRateAsync()
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql =
            """
        SELECT
            COUNT(*)
        FROM Signals
        WHERE Evaluated = 1
        """;

        long total;

        await using (var command =
            new SqliteCommand(sql, connection))
        {
            total =
                (long)(await command.ExecuteScalarAsync() ?? 0);
        }

        if (total == 0)
            return 0;

        sql =
            """
        SELECT
            COUNT(*)
        FROM Signals
        WHERE Evaluated = 1
        AND OutcomePercent > 0
        """;

        long wins;

        await using (var command =
            new SqliteCommand(sql, connection))
        {
            wins =
                (long)(await command.ExecuteScalarAsync() ?? 0);
        }

        return (double)wins / total * 100.0;
    }

    public async Task<double> GetAverageReturnAsync()
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync();

        string sql =
            """
        SELECT
            AVG(OutcomePercent)
        FROM Signals
        WHERE Evaluated = 1
        """;

        object? result;

        await using (var command =
            new SqliteCommand(sql, connection))
        {
            result =
                await command.ExecuteScalarAsync();
        }

        if (result == DBNull.Value ||
            result == null)
            return 0;

        return Convert.ToDouble(result);
    }
}