using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteCandleCacheRepository : ICandleCacheRepository
{
    private readonly string _connectionString;

    public SqliteCandleCacheRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS CandleCache
            (
                Symbol TEXT NOT NULL,
                Interval TEXT NOT NULL,
                OpenTimeUnixMs INTEGER NOT NULL,
                Open REAL NOT NULL,
                High REAL NOT NULL,
                Low REAL NOT NULL,
                Close REAL NOT NULL,
                Volume REAL NOT NULL,
                PRIMARY KEY (Symbol, Interval, OpenTimeUnixMs)
            );

            CREATE TABLE IF NOT EXISTS CandleCacheRanges
            (
                Symbol TEXT NOT NULL,
                Interval TEXT NOT NULL,
                RangeStartMs INTEGER NOT NULL,
                RangeEndMs INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CandleCacheRanges_Lookup ON CandleCacheRanges (Symbol, Interval);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsRangeCoveredAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        long startMs = ToUnixMs(startUtc);
        long endMs = ToUnixMs(endUtc);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT COUNT(*) FROM CandleCacheRanges
            WHERE Symbol = @Symbol AND Interval = @Interval AND RangeStartMs <= @StartMs AND RangeEndMs >= @EndMs
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@Interval", interval);
        command.Parameters.AddWithValue("@StartMs", startMs);
        command.Parameters.AddWithValue("@EndMs", endMs);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task<List<Candle>> GetCandlesInRangeAsync(string symbol, string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        long startMs = ToUnixMs(startUtc);
        long endMs = ToUnixMs(endUtc);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT OpenTimeUnixMs, Open, High, Low, Close, Volume
            FROM CandleCache
            WHERE Symbol = @Symbol AND Interval = @Interval AND OpenTimeUnixMs >= @StartMs AND OpenTimeUnixMs <= @EndMs
            ORDER BY OpenTimeUnixMs
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@Interval", interval);
        command.Parameters.AddWithValue("@StartMs", startMs);
        command.Parameters.AddWithValue("@EndMs", endMs);

        var result = new List<Candle>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Candle
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)).UtcDateTime,
                Open = Convert.ToDecimal(reader.GetDouble(1)),
                High = Convert.ToDecimal(reader.GetDouble(2)),
                Low = Convert.ToDecimal(reader.GetDouble(3)),
                Close = Convert.ToDecimal(reader.GetDouble(4)),
                Volume = Convert.ToDecimal(reader.GetDouble(5))
            });
        }
        return result;
    }

    public async Task SaveCandlesAsync(string symbol, string interval, DateTime rangeStartUtc, DateTime rangeEndUtc, IReadOnlyList<Candle> candles, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        const string insertCandleSql = """
            INSERT OR REPLACE INTO CandleCache (Symbol, Interval, OpenTimeUnixMs, Open, High, Low, Close, Volume)
            VALUES (@Symbol, @Interval, @OpenTimeMs, @Open, @High, @Low, @Close, @Volume)
            """;

        foreach (var candle in candles)
        {
            await using var command = new SqliteCommand(insertCandleSql, connection, transaction);
            command.Parameters.AddWithValue("@Symbol", symbol);
            command.Parameters.AddWithValue("@Interval", interval);
            command.Parameters.AddWithValue("@OpenTimeMs", ToUnixMs(candle.OpenTime));
            command.Parameters.AddWithValue("@Open", (double)candle.Open);
            command.Parameters.AddWithValue("@High", (double)candle.High);
            command.Parameters.AddWithValue("@Low", (double)candle.Low);
            command.Parameters.AddWithValue("@Close", (double)candle.Close);
            command.Parameters.AddWithValue("@Volume", (double)candle.Volume);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        long newStartMs = ToUnixMs(rangeStartUtc);
        long newEndMs = ToUnixMs(rangeEndUtc);

        // Funde a faixa nova com quaisquer faixas existentes sobrepostas/adjacentes,
        // pra não acumular fragmentos redundantes ao longo do tempo.
        const string selectOverlapping = """
            SELECT rowid, RangeStartMs, RangeEndMs FROM CandleCacheRanges
            WHERE Symbol = @Symbol AND Interval = @Interval AND RangeStartMs <= @NewEndMs AND RangeEndMs >= @NewStartMs
            """;
        await using var selectCommand = new SqliteCommand(selectOverlapping, connection, transaction);
        selectCommand.Parameters.AddWithValue("@Symbol", symbol);
        selectCommand.Parameters.AddWithValue("@Interval", interval);
        selectCommand.Parameters.AddWithValue("@NewStartMs", newStartMs);
        selectCommand.Parameters.AddWithValue("@NewEndMs", newEndMs);

        var overlappingRowIds = new List<long>();
        long mergedStart = newStartMs;
        long mergedEnd = newEndMs;

        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                overlappingRowIds.Add(reader.GetInt64(0));
                mergedStart = Math.Min(mergedStart, reader.GetInt64(1));
                mergedEnd = Math.Max(mergedEnd, reader.GetInt64(2));
            }
        }

        foreach (var rowId in overlappingRowIds)
        {
            await using var deleteCommand = new SqliteCommand("DELETE FROM CandleCacheRanges WHERE rowid = @RowId", connection, transaction);
            deleteCommand.Parameters.AddWithValue("@RowId", rowId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertRangeCommand = new SqliteCommand(
            "INSERT INTO CandleCacheRanges (Symbol, Interval, RangeStartMs, RangeEndMs) VALUES (@Symbol, @Interval, @Start, @End)",
            connection, transaction);
        insertRangeCommand.Parameters.AddWithValue("@Symbol", symbol);
        insertRangeCommand.Parameters.AddWithValue("@Interval", interval);
        insertRangeCommand.Parameters.AddWithValue("@Start", mergedStart);
        insertRangeCommand.Parameters.AddWithValue("@End", mergedEnd);
        await insertRangeCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static long ToUnixMs(DateTime dt) =>
        new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}