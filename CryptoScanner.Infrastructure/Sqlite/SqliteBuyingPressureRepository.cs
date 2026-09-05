using System.Text.Json;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteBuyingPressureRepository(string databasePath) : IBuyingPressureRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30 }.ToString();
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS BuyingPressureSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Symbol TEXT NOT NULL COLLATE NOCASE,
                    WindowEndMs INTEGER NOT NULL,
                    CollectedAtMs INTEGER NOT NULL,
                    FormulaVersion TEXT NOT NULL,
                    Score REAL, Quality TEXT NOT NULL, Details TEXT NOT NULL,
                    ReferencePrice REAL, BuyRatio REAL, Persistence REAL, PriceChangePercent REAL,
                    RelativeVolume REAL, OpenInterestChangePercent REAL, ExtensionPenalty REAL,
                    MeasurementsJson TEXT, RawDataJson TEXT NOT NULL,
                    UNIQUE(Symbol, WindowEndMs, FormulaVersion)
                );
                CREATE INDEX IF NOT EXISTS IX_PressureEntry ON BuyingPressureSnapshots(Symbol, CollectedAtMs, WindowEndMs);
                CREATE TABLE IF NOT EXISTS BuyingPressureFailures (
                    Symbol TEXT NOT NULL COLLATE NOCASE, WindowEndMs INTEGER NOT NULL,
                    FormulaVersion TEXT NOT NULL, CollectedAtMs INTEGER NOT NULL,
                    Details TEXT NOT NULL, RawDataJson TEXT NOT NULL,
                    PRIMARY KEY(Symbol,WindowEndMs,FormulaVersion)
                );
                CREATE TABLE IF NOT EXISTS BuyingPressureOutcomes (
                    SnapshotId INTEGER NOT NULL REFERENCES BuyingPressureSnapshots(Id),
                    HorizonMinutes INTEGER NOT NULL,
                    TargetTimeMs INTEGER NOT NULL,
                    Price REAL, ReturnPercent REAL, CollectedAtMs INTEGER, Reconstructed INTEGER,
                    LastAttemptMs INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(SnapshotId, HorizonMinutes)
                );
                CREATE INDEX IF NOT EXISTS IX_PressureDue ON BuyingPressureOutcomes(TargetTimeMs) WHERE Price IS NULL;
                CREATE TABLE IF NOT EXISTS BuyingPressurePrices (
                    Symbol TEXT NOT NULL COLLATE NOCASE, CloseTimeMs INTEGER NOT NULL,
                    Price REAL NOT NULL, CollectedAtMs INTEGER NOT NULL, Reconstructed INTEGER NOT NULL,
                    PRIMARY KEY(Symbol, CloseTimeMs)
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _initializeGate.Release(); }
    }

    public async Task<long> SaveAsync(BuyingPressureSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.WindowEndMs % 300_000 != 0 || snapshot.WindowEndMs > snapshot.CollectedAtMs)
            throw new ArgumentException("A janela deve estar fechada antes da coleta.");
        if (snapshot.Result.Score.HasValue && (snapshot.Result.Measurements is null ||
            snapshot.Result.Measurements.WindowEndMs != snapshot.WindowEndMs || snapshot.Result.Measurements.ReferencePrice <= 0))
            throw new ArgumentException("Uma nota válida exige referências reproduzíveis.");
        await InitializeAsync(cancellationToken);
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        using var tx = db.BeginTransaction();
        await using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO BuyingPressureSnapshots
                (Symbol, WindowEndMs, CollectedAtMs, FormulaVersion, Score, Quality, Details,
                 ReferencePrice, BuyRatio, Persistence, PriceChangePercent, RelativeVolume,
                 OpenInterestChangePercent, ExtensionPenalty, MeasurementsJson, RawDataJson)
            VALUES ($symbol,$end,$collected,$version,$score,$quality,$details,
                    $price,$buy,$persistence,$change,$volume,$oi,$penalty,$measurements,$raw)
            ON CONFLICT(Symbol, WindowEndMs, FormulaVersion) DO UPDATE SET
                CollectedAtMs=excluded.CollectedAtMs, Score=excluded.Score, Quality=excluded.Quality,
                Details=excluded.Details, ReferencePrice=excluded.ReferencePrice, BuyRatio=excluded.BuyRatio,
                Persistence=excluded.Persistence, PriceChangePercent=excluded.PriceChangePercent,
                RelativeVolume=excluded.RelativeVolume, OpenInterestChangePercent=excluded.OpenInterestChangePercent,
                ExtensionPenalty=excluded.ExtensionPenalty, MeasurementsJson=excluded.MeasurementsJson,
                RawDataJson=excluded.RawDataJson
            WHERE BuyingPressureSnapshots.Score IS NULL AND excluded.Score IS NOT NULL;
            SELECT Id FROM BuyingPressureSnapshots WHERE Symbol=$symbol AND WindowEndMs=$end AND FormulaVersion=$version;
            """;
        var m = snapshot.Result.Measurements;
        cmd.Parameters.AddWithValue("$symbol", snapshot.Symbol.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$end", snapshot.WindowEndMs);
        cmd.Parameters.AddWithValue("$collected", snapshot.CollectedAtMs);
        cmd.Parameters.AddWithValue("$version", BuyingPressureSnapshot.FormulaVersion);
        AddDecimal(cmd, "$score", snapshot.Result.Score);
        cmd.Parameters.AddWithValue("$quality", snapshot.Result.Score.HasValue ? "Available" : "Unavailable");
        cmd.Parameters.AddWithValue("$details", snapshot.Result.Details);
        AddDecimal(cmd, "$price", m?.ReferencePrice); AddDecimal(cmd, "$buy", m?.BuyRatio);
        AddDecimal(cmd, "$persistence", m?.Persistence); AddDecimal(cmd, "$change", m?.PriceChangePercent);
        AddDecimal(cmd, "$volume", m?.RelativeVolume); AddDecimal(cmd, "$oi", m?.OpenInterestChangePercent);
        AddDecimal(cmd, "$penalty", m?.ExtensionPenalty);
        cmd.Parameters.AddWithValue("$measurements", m is null ? DBNull.Value : JsonSerializer.Serialize(m));
        cmd.Parameters.AddWithValue("$raw", JsonSerializer.Serialize(snapshot.RawData));
        long id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        if (!snapshot.Result.Score.HasValue)
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO BuyingPressureFailures(Symbol,WindowEndMs,FormulaVersion,CollectedAtMs,Details,RawDataJson)
                VALUES($symbol,$end,$version,$collected,$details,$raw);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        // First valid reading is immutable. Recovery from a failure gets its actual availability time.
        cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$id", id);
        cmd.CommandText = """
            INSERT OR IGNORE INTO BuyingPressureOutcomes(SnapshotId,HorizonMinutes,TargetTimeMs)
            SELECT s.Id,h.Minutes,s.WindowEndMs+h.Minutes*60000
            FROM BuyingPressureSnapshots s CROSS JOIN
                (SELECT 30 AS Minutes UNION ALL SELECT 60 UNION ALL SELECT 240 UNION ALL SELECT 1440) h
            WHERE s.Id=$id AND s.Score IS NOT NULL AND s.ReferencePrice>0;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await CompleteFromPricesAsync(db, tx, snapshot.Symbol, cancellationToken);
        tx.Commit();
        return id;
    }

    public async Task SavePricesAsync(IReadOnlyList<PressurePrice> prices, CancellationToken cancellationToken = default)
    {
        if (prices.Count == 0) return;
        await InitializeAsync(cancellationToken);
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        using var tx = db.BeginTransaction();
        await using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO BuyingPressurePrices(Symbol,CloseTimeMs,Price,CollectedAtMs,Reconstructed)
            VALUES ($symbol,$time,$price,$collected,$reconstructed);
            """;
        foreach (var p in prices)
        {
            if (p.Price <= 0 || p.CloseTimeMs % 300_000 != 0 || p.CloseTimeMs > p.CollectedAtMs)
                throw new ArgumentException("O preço deve corresponder a um fechamento válido.");
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$symbol", p.Symbol.ToUpperInvariant());
            cmd.Parameters.AddWithValue("$time", p.CloseTimeMs);
            AddDecimal(cmd, "$price", p.Price);
            cmd.Parameters.AddWithValue("$collected", p.CollectedAtMs);
            cmd.Parameters.AddWithValue("$reconstructed", p.Reconstructed ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (string symbol in prices.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase))
            await CompleteFromPricesAsync(db, tx, symbol, cancellationToken);
        tx.Commit();
    }

    private static async Task CompleteFromPricesAsync(SqliteConnection db, SqliteTransaction tx, string symbol, CancellationToken cancellationToken)
    {
        await using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.CommandText = """
            UPDATE BuyingPressureOutcomes AS o
            SET Price=p.Price, ReturnPercent=(p.Price-s.ReferencePrice)/s.ReferencePrice*100.0,
                CollectedAtMs=p.CollectedAtMs, Reconstructed=p.Reconstructed
            FROM BuyingPressureSnapshots AS s JOIN BuyingPressurePrices AS p ON p.Symbol=s.Symbol
            WHERE s.Symbol=$symbol COLLATE NOCASE AND o.SnapshotId=s.Id AND p.CloseTimeMs=o.TargetTimeMs AND o.Price IS NULL
                AND s.ReferencePrice>0 AND p.CollectedAtMs>=o.TargetTimeMs;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PressurePriceTarget>> GetDueTargetsAsync(long nowMs, int limit, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        using var tx = db.BeginTransaction();
        await using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT s.Symbol,o.TargetTimeMs FROM BuyingPressureOutcomes o
            JOIN BuyingPressureSnapshots s ON s.Id=o.SnapshotId
            WHERE o.Price IS NULL AND o.TargetTimeMs <= $now AND o.LastAttemptMs <= $now-300000
            GROUP BY s.Symbol,o.TargetTimeMs ORDER BY MIN(o.LastAttemptMs),o.TargetTimeMs LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$now", nowMs); cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        var result = new List<PressurePriceTarget>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0), reader.GetInt64(1)));
        foreach (var target in result)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$now", nowMs); cmd.Parameters.AddWithValue("$symbol", target.Symbol);
            cmd.Parameters.AddWithValue("$time", target.CloseTimeMs);
            cmd.CommandText = """
                UPDATE BuyingPressureOutcomes SET LastAttemptMs=$now
                WHERE Price IS NULL AND TargetTimeMs=$time AND SnapshotId IN
                    (SELECT Id FROM BuyingPressureSnapshots WHERE Symbol=$symbol);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        tx.Commit();
        return result;
    }

    private static void AddDecimal(SqliteCommand cmd, string name, decimal? value) =>
        cmd.Parameters.AddWithValue(name, value.HasValue ? (object)(double)value.Value : DBNull.Value);
}
