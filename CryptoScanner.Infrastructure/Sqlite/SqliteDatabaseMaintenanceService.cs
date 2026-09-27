using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed record DatabaseStorageSnapshot(
    string StateDatabasePath,
    long StateDatabaseBytes,
    string CandleCachePath,
    long CandleCacheBytes,
    long CandleCacheHighestRowId,
    long PressureSnapshotHighestRowId,
    long PressureOutcomeHighestRowId,
    long PressurePriceHighestRowId);

/// <summary>
/// Manutenção explícita do armazenamento local. Não toca em sinais, trades,
/// laboratório, LLM ou resultados de backtest.
/// </summary>
public sealed class SqliteDatabaseMaintenanceService
{
    private readonly string _stateDatabasePath;
    private readonly string _candleCachePath;

    public SqliteDatabaseMaintenanceService(string stateDatabasePath, string candleCachePath)
    {
        _stateDatabasePath = stateDatabasePath;
        _candleCachePath = candleCachePath;
    }

    public async Task<DatabaseStorageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return new DatabaseStorageSnapshot(
            _stateDatabasePath,
            FileSize(_stateDatabasePath),
            _candleCachePath,
            FileSize(_candleCachePath),
            await HighestRowIdAsync(_stateDatabasePath, "CandleCache", cancellationToken),
            await HighestRowIdAsync(_stateDatabasePath, "BuyingPressureSnapshots", cancellationToken),
            await HighestRowIdAsync(_stateDatabasePath, "BuyingPressureOutcomes", cancellationToken),
            await HighestRowIdAsync(_stateDatabasePath, "BuyingPressurePrices", cancellationToken));
    }

    public async Task ClearCandleCachesAndCompactAsync(CancellationToken cancellationToken = default)
    {
        var paths = new[] { _stateDatabasePath, _candleCachePath }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists);

        foreach (string path in paths)
        {
            await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                DefaultTimeout = 60
            }.ToString());
            await db.OpenAsync(cancellationToken);

            if (!await TableExistsAsync(db, "CandleCache", cancellationToken))
                continue;

            await ExecuteAsync(db, "DELETE FROM CandleCache; DELETE FROM CandleCacheRanges;", cancellationToken);
            await ExecuteAsync(db, "VACUUM;", cancellationToken);
        }
    }

    public async Task PruneBuyingPressureAndCompactAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateDatabasePath)) return;

        long cutoffMs = new DateTimeOffset(DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _stateDatabasePath,
            DefaultTimeout = 60
        }.ToString());
        await db.OpenAsync(cancellationToken);

        if (!await TableExistsAsync(db, "BuyingPressureSnapshots", cancellationToken)) return;

        await ExecuteAsync(db, """
            DELETE FROM BuyingPressureOutcomes
            WHERE SnapshotId IN (SELECT Id FROM BuyingPressureSnapshots WHERE WindowEndMs < $cutoff);
            DELETE FROM BuyingPressureSnapshots WHERE WindowEndMs < $cutoff;
            DELETE FROM BuyingPressureFailures WHERE WindowEndMs < $cutoff;
            DELETE FROM BuyingPressurePrices WHERE CloseTimeMs < $cutoff;
            """, cancellationToken, ("$cutoff", cutoffMs));
        await ExecuteAsync(db, "VACUUM;", cancellationToken);
    }

    private static async Task<long> HighestRowIdAsync(string path, string table, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return 0;
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await db.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(db, table, cancellationToken)) return 0;
        await using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT IFNULL(MAX(rowid), 0) FROM [{table}]";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection db, string table, CancellationToken cancellationToken)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name)";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task ExecuteAsync(SqliteConnection db, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
