using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteBacktestRunResultRepository : IBacktestRunResultRepository
{
    private readonly string _connectionString;

    public SqliteBacktestRunResultRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS BacktestRunResults
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SignatureHash TEXT NOT NULL UNIQUE,
                SavedAt TEXT NOT NULL,
                Label TEXT,
                Profile TEXT,
                RiskMode TEXT,
                StartDate TEXT,
                EndDate TEXT,
                Symbols TEXT,
                SymbolCount INTEGER,
                MinScore REAL,
                MinResistanceDistanceSwing REAL,
                MinResistanceDistanceAtr REAL,
                MinVolumeSpike REAL,
                MinRiskReward REAL,
                MinStopDistancePercent REAL,
                MaxRiskReward REAL,
                EnablePullbackBounce INTEGER,
                EvaluationHoursOverride INTEGER,
                TotalTrades INTEGER,
                WinRate REAL,
                TotalReturnPercent REAL,
                MaxDrawdownPercent REAL,
                ProfitFactor REAL,
                AvgRiskRewardAtEntry REAL,
                BreakEvenWinRate REAL,
                Edge REAL
            );
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migração: adiciona a coluna nova em bancos criados por uma versão anterior.
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragmaCommand = new SqliteCommand("PRAGMA table_info(BacktestRunResults)", connection))
        await using (var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                existingColumns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        if (!existingColumns.Contains("EnableBollingerScoring"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN EnableBollingerScoring INTEGER DEFAULT 0", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("EnableVolatilityScoringPhaseB"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN EnableVolatilityScoringPhaseB INTEGER DEFAULT 0", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("Diagnostics"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN Diagnostics TEXT DEFAULT ''", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("Tp1Fraction"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN Tp1Fraction REAL", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("Tp2Fraction"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN Tp2Fraction REAL", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("MaxStopDistancePercent"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN MaxStopDistancePercent REAL", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existingColumns.Contains("DisableTimeout"))
        {
            await using var alterCommand = new SqliteCommand(
                "ALTER TABLE BacktestRunResults ADD COLUMN DisableTimeout INTEGER DEFAULT 0", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(string signatureHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT COUNT(*) FROM BacktestRunResults WHERE SignatureHash = @Hash", connection);
        command.Parameters.AddWithValue("@Hash", signatureHash);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) > 0;
    }

    public async Task SaveAsync(BacktestRunResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT OR IGNORE INTO BacktestRunResults
            (SignatureHash, SavedAt, Label, Profile, RiskMode, StartDate, EndDate, Symbols, SymbolCount,
             MinScore, MinResistanceDistanceSwing, MinResistanceDistanceAtr, MinVolumeSpike, MinRiskReward,
             MinStopDistancePercent, MaxRiskReward, EnablePullbackBounce, EnableBollingerScoring, EnableVolatilityScoringPhaseB, EvaluationHoursOverride,
             TotalTrades, WinRate, TotalReturnPercent, MaxDrawdownPercent, ProfitFactor,
             AvgRiskRewardAtEntry, BreakEvenWinRate, Edge, Diagnostics, Tp1Fraction, Tp2Fraction, MaxStopDistancePercent, DisableTimeout)
            VALUES
            (@SignatureHash, @SavedAt, @Label, @Profile, @RiskMode, @StartDate, @EndDate, @Symbols, @SymbolCount,
             @MinScore, @MinResistanceDistanceSwing, @MinResistanceDistanceAtr, @MinVolumeSpike, @MinRiskReward,
             @MinStopDistancePercent, @MaxRiskReward, @EnablePullbackBounce, @EnableBollingerScoring, @EnableVolatilityScoringPhaseB, @EvaluationHoursOverride,
             @TotalTrades, @WinRate, @TotalReturnPercent, @MaxDrawdownPercent, @ProfitFactor,
             @AvgRiskRewardAtEntry, @BreakEvenWinRate, @Edge, @Diagnostics, @Tp1Fraction, @Tp2Fraction, @MaxStopDistancePercent, @DisableTimeout)
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@SignatureHash", result.SignatureHash);
        command.Parameters.AddWithValue("@SavedAt", result.SavedAt.ToString("O"));
        command.Parameters.AddWithValue("@Label", result.Label ?? "");
        command.Parameters.AddWithValue("@Profile", result.Profile ?? "");
        command.Parameters.AddWithValue("@RiskMode", result.RiskMode ?? "");
        command.Parameters.AddWithValue("@StartDate", result.StartDate.ToString("O"));
        command.Parameters.AddWithValue("@EndDate", result.EndDate.ToString("O"));
        command.Parameters.AddWithValue("@Symbols", result.Symbols ?? "");
        command.Parameters.AddWithValue("@SymbolCount", result.SymbolCount);
        command.Parameters.AddWithValue("@MinScore", (double)result.MinScore);
        command.Parameters.AddWithValue("@MinResistanceDistanceSwing", (double)result.MinResistanceDistanceSwing);
        command.Parameters.AddWithValue("@MinResistanceDistanceAtr", (double)result.MinResistanceDistanceAtr);
        command.Parameters.AddWithValue("@MinVolumeSpike", (double)result.MinVolumeSpike);
        command.Parameters.AddWithValue("@MinRiskReward", (double)result.MinRiskReward);
        command.Parameters.AddWithValue("@MinStopDistancePercent", (double)result.MinStopDistancePercent);
        command.Parameters.AddWithValue("@MaxRiskReward", (double)result.MaxRiskReward);
        command.Parameters.AddWithValue("@EnablePullbackBounce", result.EnablePullbackBounce ? 1 : 0);
        command.Parameters.AddWithValue("@EnableBollingerScoring", result.EnableBollingerScoring ? 1 : 0);
        command.Parameters.AddWithValue("@EnableVolatilityScoringPhaseB", result.EnableVolatilityScoringPhaseB ? 1 : 0);
        command.Parameters.AddWithValue("@Diagnostics", result.Diagnostics ?? "");
        command.Parameters.AddWithValue("@Tp1Fraction", (object?)result.Tp1Fraction ?? DBNull.Value);
        command.Parameters.AddWithValue("@Tp2Fraction", (object?)result.Tp2Fraction ?? DBNull.Value);
        command.Parameters.AddWithValue("@MaxStopDistancePercent", (object?)result.MaxStopDistancePercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@DisableTimeout", result.DisableTimeout ? 1 : 0);
        command.Parameters.AddWithValue("@EvaluationHoursOverride", (object?)result.EvaluationHoursOverride ?? DBNull.Value);
        command.Parameters.AddWithValue("@TotalTrades", result.TotalTrades);
        command.Parameters.AddWithValue("@WinRate", result.WinRate);
        command.Parameters.AddWithValue("@TotalReturnPercent", (double)result.TotalReturnPercent);
        command.Parameters.AddWithValue("@MaxDrawdownPercent", (double)result.MaxDrawdownPercent);
        command.Parameters.AddWithValue("@ProfitFactor", (double)result.ProfitFactor);
        command.Parameters.AddWithValue("@AvgRiskRewardAtEntry", (double)result.AvgRiskRewardAtEntry);
        command.Parameters.AddWithValue("@BreakEvenWinRate", result.BreakEvenWinRate);
        command.Parameters.AddWithValue("@Edge", result.Edge);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BacktestRunResult>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<BacktestRunResult>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT * FROM BacktestRunResults ORDER BY SavedAt DESC", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BacktestRunResult
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                SignatureHash = reader.GetString(reader.GetOrdinal("SignatureHash")),
                SavedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("SavedAt"))),
                Label = GetStringOrDefault(reader, "Label"),
                Profile = GetStringOrDefault(reader, "Profile"),
                RiskMode = GetStringOrDefault(reader, "RiskMode"),
                StartDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("StartDate"))),
                EndDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("EndDate"))),
                Symbols = GetStringOrDefault(reader, "Symbols"),
                SymbolCount = reader.GetInt32(reader.GetOrdinal("SymbolCount")),
                MinScore = GetDecimalSafe(reader, "MinScore"),
                MinResistanceDistanceSwing = GetDecimalSafe(reader, "MinResistanceDistanceSwing"),
                MinResistanceDistanceAtr = GetDecimalSafe(reader, "MinResistanceDistanceAtr"),
                MinVolumeSpike = GetDecimalSafe(reader, "MinVolumeSpike"),
                MinRiskReward = GetDecimalSafe(reader, "MinRiskReward"),
                MinStopDistancePercent = GetDecimalSafe(reader, "MinStopDistancePercent"),
                MaxRiskReward = GetDecimalSafe(reader, "MaxRiskReward"),
                EnablePullbackBounce = reader.GetInt32(reader.GetOrdinal("EnablePullbackBounce")) == 1,
                EnableBollingerScoring = !reader.IsDBNull(reader.GetOrdinal("EnableBollingerScoring")) && reader.GetInt32(reader.GetOrdinal("EnableBollingerScoring")) == 1,
                EnableVolatilityScoringPhaseB = !reader.IsDBNull(reader.GetOrdinal("EnableVolatilityScoringPhaseB")) && reader.GetInt32(reader.GetOrdinal("EnableVolatilityScoringPhaseB")) == 1,
                Diagnostics = reader.IsDBNull(reader.GetOrdinal("Diagnostics")) ? "" : reader.GetString(reader.GetOrdinal("Diagnostics")),
                Tp1Fraction = reader.IsDBNull(reader.GetOrdinal("Tp1Fraction")) ? null : (decimal?)reader.GetDouble(reader.GetOrdinal("Tp1Fraction")),
                Tp2Fraction = reader.IsDBNull(reader.GetOrdinal("Tp2Fraction")) ? null : (decimal?)reader.GetDouble(reader.GetOrdinal("Tp2Fraction")),
                MaxStopDistancePercent = reader.IsDBNull(reader.GetOrdinal("MaxStopDistancePercent")) ? null : (decimal?)reader.GetDouble(reader.GetOrdinal("MaxStopDistancePercent")),
                DisableTimeout = !reader.IsDBNull(reader.GetOrdinal("DisableTimeout")) && reader.GetInt32(reader.GetOrdinal("DisableTimeout")) == 1,
                EvaluationHoursOverride = reader.IsDBNull(reader.GetOrdinal("EvaluationHoursOverride")) ? null : reader.GetInt32(reader.GetOrdinal("EvaluationHoursOverride")),
                TotalTrades = reader.GetInt32(reader.GetOrdinal("TotalTrades")),
                WinRate = reader.GetDouble(reader.GetOrdinal("WinRate")),
                TotalReturnPercent = GetDecimalSafe(reader, "TotalReturnPercent"),
                MaxDrawdownPercent = GetDecimalSafe(reader, "MaxDrawdownPercent"),
                ProfitFactor = GetDecimalSafe(reader, "ProfitFactor"),
                AvgRiskRewardAtEntry = GetDecimalSafe(reader, "AvgRiskRewardAtEntry"),
                BreakEvenWinRate = reader.GetDouble(reader.GetOrdinal("BreakEvenWinRate")),
                Edge = reader.GetDouble(reader.GetOrdinal("Edge"))
            });
        }

        return result;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("DELETE FROM BacktestRunResults WHERE Id = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetStringOrDefault(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    /// <summary>
    /// Lê um valor decimal do SQLite (guardado como double/REAL) com proteção contra
    /// overflow — registros antigos gravados antes de uma correção anterior podem ter
    /// valores como decimal.MaxValue que não sobrevivem intactos à ida-e-volta via double.
    /// Em vez de travar o Histórico inteiro por causa de 1 linha ruim, usa um valor seguro.
    /// </summary>
    private static decimal GetDecimalSafe(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return 0m;

        try
        {
            return Convert.ToDecimal(reader.GetDouble(ordinal));
        }
        catch (OverflowException)
        {
            return 999999m;
        }
    }
}