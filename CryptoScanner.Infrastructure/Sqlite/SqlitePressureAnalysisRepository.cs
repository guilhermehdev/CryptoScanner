using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqlitePressureAnalysisRepository(string databasePath)
{
    public const int HistoryLimit = 500;
    private readonly SqliteBuyingPressureRepository _schema = new(databasePath);
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30 }.ToString();
    private const string Filtered = """
        WITH filtered AS (
            SELECT s.*,o.ReturnPercent,o.Reconstructed,
                COALESCE(o.TargetTimeMs,s.WindowEndMs+$horizon*60000) AS DueMs
            FROM BuyingPressureSnapshots s LEFT JOIN BuyingPressureOutcomes o
                ON o.SnapshotId=s.Id AND o.HorizonMinutes=$horizon
            WHERE s.CollectedAtMs >= $from AND s.CollectedAtMs < $to
                AND ($symbol='' OR s.Symbol=$symbol COLLATE NOCASE) AND s.FormulaVersion=$version
        )
        """;

    public async Task<PressureAnalysisReport> LoadAsync(PressureAnalysisFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter.FromMs >= filter.ToMs || !new[] { 30, 60, 240, 1440 }.Contains(filter.HorizonMinutes) ||
            string.IsNullOrWhiteSpace(filter.FormulaVersion)) throw new ArgumentException("Filtros inválidos.");
        await _schema.InitializeAsync(cancellationToken);
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        // A consistent read snapshot across summary, bands and history during concurrent scans.
        using var transaction = db.BeginTransaction(deferred: true);
        await using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
        cmd.Parameters.AddWithValue("$from", filter.FromMs); cmd.Parameters.AddWithValue("$to", filter.ToMs);
        cmd.Parameters.AddWithValue("$symbol", filter.Symbol.Trim()); cmd.Parameters.AddWithValue("$version", filter.FormulaVersion);
        cmd.Parameters.AddWithValue("$horizon", filter.HorizonMinutes);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.CommandText = Filtered + " SELECT COUNT(*),COALESCE(SUM(Score IS NULL),0) FROM filtered;";
        long total, unavailable;
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        { await reader.ReadAsync(cancellationToken); total=reader.GetInt64(0); unavailable=reader.GetInt64(1); }
        cmd.CommandText = Filtered + """
            SELECT MIN(CAST(Score/10 AS INTEGER),9) AS Band, COUNT(*),COUNT(ReturnPercent),
                SUM(ReturnPercent IS NULL AND DueMs>$now),SUM(ReturnPercent IS NULL AND DueMs<=$now),
                SUM(CASE WHEN ReturnPercent>0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN ReturnPercent IS NOT NULL AND Reconstructed=1 THEN 1 ELSE 0 END),
                AVG(ReturnPercent),MIN(ReturnPercent),MAX(ReturnPercent)
            FROM filtered WHERE Score IS NOT NULL GROUP BY Band ORDER BY Band;
            """;
        var bands = new List<PressureBand>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                bands.Add(new(reader.GetInt32(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),
                    reader.GetInt64(4),reader.GetInt64(5),reader.GetInt64(6),Number(reader,7),Number(reader,8),Number(reader,9)));
        cmd.CommandText = Filtered + """
            SELECT Id,Symbol,WindowEndMs,CollectedAtMs,Score,ReferencePrice,ReturnPercent,
                CASE WHEN Score IS NULL THEN 'Sem dados'
                     WHEN ReturnPercent IS NOT NULL THEN 'Avaliada'
                     WHEN DueMs>$now THEN 'Aguardando prazo' ELSE 'Aguardando recuperação' END,
                CASE WHEN ReturnPercent IS NULL THEN '—'
                     WHEN Reconstructed=1 THEN 'Histórico recuperado' ELSE 'Coleta regular' END,Details
            FROM filtered ORDER BY CollectedAtMs DESC,Id DESC LIMIT 500;
            """;
        var history = new List<PressureHistoryRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                history.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2),reader.GetInt64(3),
                    Number(reader,4),Number(reader,5),Number(reader,6),reader.GetString(7),reader.GetString(8),reader.GetString(9)));
        transaction.Commit();
        return new(total,unavailable,bands,history);
    }

    private static decimal? Number(SqliteDataReader reader,int column) => reader.IsDBNull(column) ? null : Convert.ToDecimal(reader.GetDouble(column));
}
