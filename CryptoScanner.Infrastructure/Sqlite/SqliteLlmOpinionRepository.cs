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
                Risks TEXT NOT NULL DEFAULT '',
                SimulatedTradeId INTEGER NULL,
                OutcomeEvaluated INTEGER NOT NULL DEFAULT 0,
                OutcomePercent REAL NULL,
            OutcomeReason TEXT NOT NULL DEFAULT '',
            OutcomeAt TEXT NULL,
            SnapshotJson TEXT NOT NULL DEFAULT '',
            ValidationStatus TEXT NOT NULL DEFAULT '',
            ValidationMessage TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS IX_LlmOpinions_CreatedAt ON LlmOpinions(CreatedAt DESC);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "SimulatedTradeId", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "OutcomeEvaluated", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "OutcomePercent", "REAL NULL", cancellationToken);
        await EnsureColumnAsync(connection, "OutcomeReason", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "OutcomeAt", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "SnapshotJson", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ValidationStatus", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ValidationMessage", "TEXT NOT NULL DEFAULT ''", cancellationToken);
    }

    public async Task<long> AddAsync(LlmOpinionRecord opinion, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO LlmOpinions
            (CreatedAt, Symbol, Profile, ImagePath, AnalysisPrice, ScannerSignal, Decision, Direction,
             Confidence, Trend, Entry, Stop, Tp1, Tp2, Reasons, Risks, SnapshotJson, ValidationStatus, ValidationMessage, SimulatedTradeId, OutcomeEvaluated, OutcomePercent, OutcomeReason, OutcomeAt)
            VALUES
            (@CreatedAt, @Symbol, @Profile, @ImagePath, @AnalysisPrice, @ScannerSignal, @Decision, @Direction,
             @Confidence, @Trend, @Entry, @Stop, @Tp1, @Tp2, @Reasons, @Risks, @SnapshotJson, @ValidationStatus, @ValidationMessage, @SimulatedTradeId, @OutcomeEvaluated, @OutcomePercent, @OutcomeReason, @OutcomeAt);
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
        command.Parameters.AddWithValue("@SnapshotJson", opinion.SnapshotJson);
        command.Parameters.AddWithValue("@ValidationStatus", opinion.ValidationStatus);
        command.Parameters.AddWithValue("@ValidationMessage", opinion.ValidationMessage);
        command.Parameters.AddWithValue("@SimulatedTradeId", (object?)opinion.SimulatedTradeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@OutcomeEvaluated", opinion.OutcomeEvaluated ? 1 : 0);
        command.Parameters.AddWithValue("@OutcomePercent", (object?)opinion.OutcomePercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@OutcomeReason", opinion.OutcomeReason);
        command.Parameters.AddWithValue("@OutcomeAt", opinion.OutcomeAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
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
            result.Add(Map(reader));
        }
        return result;
    }

    public async Task<IReadOnlyList<LlmOpinionRecord>> GetPendingRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT * FROM LlmOpinions
            WHERE OutcomeEvaluated = 0
              AND SimulatedTradeId IS NULL
              AND ValidationStatus = 'VALIDA'
              AND Decision IN ('COMPRA', 'VENDA')
            ORDER BY CreatedAt ASC
            """;
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LlmOpinionRecord>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Map(reader));
        return result;
    }

    public async Task AttachTradeAsync(long opinionId, int simulatedTradeId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(
            "UPDATE LlmOpinions SET SimulatedTradeId = @TradeId WHERE Id = @Id AND SimulatedTradeId IS NULL",
            connection);
        command.Parameters.AddWithValue("@Id", opinionId);
        command.Parameters.AddWithValue("@TradeId", simulatedTradeId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateOutcomeAsync(int simulatedTradeId, decimal outcomePercent, string outcomeReason, DateTime outcomeAt, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(
            "UPDATE LlmOpinions SET OutcomeEvaluated = 1, OutcomePercent = @Percent, OutcomeReason = @Reason, OutcomeAt = @At WHERE SimulatedTradeId = @TradeId",
            connection);
        command.Parameters.AddWithValue("@TradeId", simulatedTradeId);
        command.Parameters.AddWithValue("@Percent", (double)outcomePercent);
        command.Parameters.AddWithValue("@Reason", outcomeReason);
        command.Parameters.AddWithValue("@At", outcomeAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateOpinionOutcomeAsync(long opinionId, decimal outcomePercent, string outcomeReason, DateTime outcomeAt, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(
            "UPDATE LlmOpinions SET OutcomeEvaluated = 1, OutcomePercent = @Percent, OutcomeReason = @Reason, OutcomeAt = @At WHERE Id = @Id AND OutcomeEvaluated = 0",
            connection);
        command.Parameters.AddWithValue("@Id", opinionId);
        command.Parameters.AddWithValue("@Percent", (double)outcomePercent);
        command.Parameters.AddWithValue("@Reason", outcomeReason);
        command.Parameters.AddWithValue("@At", outcomeAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string column, string definition, CancellationToken cancellationToken)
    {
        await using var check = new SqliteCommand("SELECT COUNT(*) FROM pragma_table_info('LlmOpinions') WHERE name = @Name", connection);
        check.Parameters.AddWithValue("@Name", column);
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
            return;

        await using var alter = new SqliteCommand($"ALTER TABLE LlmOpinions ADD COLUMN {column} {definition}", connection);
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LlmOpinionRecord Map(SqliteDataReader reader) => new()
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
        Entry = ReadNullableDecimal(reader, "Entry"), Stop = ReadNullableDecimal(reader, "Stop"),
        Tp1 = ReadNullableDecimal(reader, "Tp1"), Tp2 = ReadNullableDecimal(reader, "Tp2"),
        Reasons = reader.GetString(reader.GetOrdinal("Reasons")), Risks = reader.GetString(reader.GetOrdinal("Risks")),
        SnapshotJson = ReadString(reader, "SnapshotJson"), ValidationStatus = ReadString(reader, "ValidationStatus"),
        ValidationMessage = ReadString(reader, "ValidationMessage"), SimulatedTradeId = ReadNullableInt(reader, "SimulatedTradeId"),
        OutcomeEvaluated = reader.GetInt32(reader.GetOrdinal("OutcomeEvaluated")) == 1,
        OutcomePercent = ReadNullableDecimal(reader, "OutcomePercent"), OutcomeReason = reader.GetString(reader.GetOrdinal("OutcomeReason")),
        OutcomeAt = ReadNullableDateTime(reader, "OutcomeAt")
    };
    private static int? ReadNullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string ReadString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
    }
    private static decimal? ReadNullableDecimal(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }
}
