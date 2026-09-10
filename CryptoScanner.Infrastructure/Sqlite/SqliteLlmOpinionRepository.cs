using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteLlmOpinionRepository : ILlmOpinionRepository
{
    private readonly string _connectionString;

    public SqliteLlmOpinionRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS LlmOpinions
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedAt TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                Profile TEXT NOT NULL DEFAULT '',
                ImagePath TEXT NOT NULL DEFAULT '',
                AnalysisPrice REAL NOT NULL,
                ScannerSignal TEXT NOT NULL DEFAULT '',
                Decision TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Confidence INTEGER NOT NULL,
                Trend TEXT NOT NULL DEFAULT '',
                Entry REAL NULL,
                Stop REAL NULL,
                Tp1 REAL NULL,
                Tp2 REAL NULL,
                Reasons TEXT NOT NULL DEFAULT '',
                Risks TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS IX_LlmOpinions_CreatedAt ON LlmOpinions(CreatedAt DESC);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> AddAsync(LlmOpinionRecord opinion, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO LlmOpinions
            (CreatedAt, Symbol, Profile, ImagePath, AnalysisPrice, ScannerSignal, Decision, Direction,
             Confidence, Trend, Entry, Stop, Tp1, Tp2, Reasons, Risks)
            VALUES
            (@CreatedAt, @Symbol, @Profile, @ImagePath, @AnalysisPrice, @ScannerSignal, @Decision, @Direction,
             @Confidence, @Trend, @Entry, @Stop, @Tp1, @Tp2, @Reasons, @Risks);
            SELECT last_insert_rowid();
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@CreatedAt", opinion.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@Symbol", opinion.Symbol);
        command.Parameters.AddWithValue("@Profile", opinion.Profile);
        command.Parameters.AddWithValue("@ImagePath", opinion.ImagePath);
        command.Parameters.AddWithValue("@AnalysisPrice", opinion.AnalysisPrice);
        command.Parameters.AddWithValue("@ScannerSignal", opinion.ScannerSignal);
        command.Parameters.AddWithValue("@Decision", opinion.Decision);
        command.Parameters.AddWithValue("@Direction", opinion.Direction);
        command.Parameters.AddWithValue("@Confidence", opinion.Confidence);
        command.Parameters.AddWithValue("@Trend", opinion.Trend);
        command.Parameters.AddWithValue("@Entry", (object?)opinion.Entry ?? DBNull.Value);
        command.Parameters.AddWithValue("@Stop", (object?)opinion.Stop ?? DBNull.Value);
        command.Parameters.AddWithValue("@Tp1", (object?)opinion.Tp1 ?? DBNull.Value);
        command.Parameters.AddWithValue("@Tp2", (object?)opinion.Tp2 ?? DBNull.Value);
        command.Parameters.AddWithValue("@Reasons", opinion.Reasons);
        command.Parameters.AddWithValue("@Risks", opinion.Risks);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<IReadOnlyList<LlmOpinionRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand($"SELECT * FROM LlmOpinions ORDER BY CreatedAt DESC LIMIT {limit}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LlmOpinionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new LlmOpinionRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime(),
                Symbol = reader.GetString(reader.GetOrdinal("Symbol")),
                Profile = reader.GetString(reader.GetOrdinal("Profile")),
                ImagePath = reader.GetString(reader.GetOrdinal("ImagePath")),
                AnalysisPrice = reader.GetDecimal(reader.GetOrdinal("AnalysisPrice")),
                ScannerSignal = reader.GetString(reader.GetOrdinal("ScannerSignal")),
                Decision = reader.GetString(reader.GetOrdinal("Decision")),
                Direction = reader.GetString(reader.GetOrdinal("Direction")),
                Confidence = reader.GetInt32(reader.GetOrdinal("Confidence")),
                Trend = reader.GetString(reader.GetOrdinal("Trend")),
                Entry = ReadNullableDecimal(reader, "Entry"),
                Stop = ReadNullableDecimal(reader, "Stop"),
                Tp1 = ReadNullableDecimal(reader, "Tp1"),
                Tp2 = ReadNullableDecimal(reader, "Tp2"),
                Reasons = reader.GetString(reader.GetOrdinal("Reasons")),
                Risks = reader.GetString(reader.GetOrdinal("Risks"))
            });
        }
        return result;
    }

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }
}
