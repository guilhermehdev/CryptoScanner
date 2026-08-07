using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteSimulatedTradeRepository : ISimulatedTradeRepository
{
    private readonly string _connectionString;

    public SqliteSimulatedTradeRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Cria a tabela do zero (caso não exista) só com as colunas mínimas obrigatórias —
        // as demais são adicionadas logo abaixo via migração, cobrindo tanto bancos novos
        // quanto bancos criados por uma versão anterior desse app (sem essas colunas).
        const string createSql = """
            CREATE TABLE IF NOT EXISTS SimulatedTrades
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL,
                EntryTime TEXT NOT NULL,
                EntryPrice REAL NOT NULL,
                TakeProfit REAL NOT NULL,
                StopLoss REAL NOT NULL,
                Closed INTEGER DEFAULT 0
            );
            """;
        await using (var createCommand = new SqliteCommand(createSql, connection))
            await createCommand.ExecuteNonQueryAsync(cancellationToken);

        var expectedColumns = new (string Name, string Type)[]
        {
            ("Note", "TEXT"),
            ("Profile", "TEXT"),
            ("ScoreAtEntry", "REAL"),
            ("Rsi", "REAL"),
            ("Adx", "REAL"),
            ("AtrPercent", "REAL"),
            ("EmaDistanceAtr", "REAL"),
            ("SwingUsageAtr", "REAL"),
            ("VolumeSpike", "REAL"),
            ("VolumeImbalance", "REAL"),
            ("RelativeStrength", "REAL"),
            ("RiskRewardAtEntry", "REAL"),
            ("TrendScore", "REAL"),
            ("StructureScore", "REAL"),
            ("VolumeScore", "REAL"),
            ("CandleScore", "REAL"),
            ("SetupScore", "REAL"),
            ("MomentumScore", "REAL"),
            ("VolatilityScore", "REAL"),
            ("TrendStrengthScore", "REAL"),
            ("PatternName", "TEXT"),
            ("SmartMoneyLabel", "TEXT"),
            ("BreakoutSource", "TEXT"),
            ("MarketRegime", "TEXT"),
            ("IsBullTrap", "INTEGER DEFAULT 0"),
            ("IsBearTrap", "INTEGER DEFAULT 0"),
            ("ExitTime", "TEXT"),
            ("ExitPrice", "REAL"),
            ("OutcomePercent", "REAL"),
            ("ExitReason", "TEXT"),
            // Etapa 3.3 — estado da saída parcial (TP1→breakeven→TP2→TP3).
            ("TakeProfit1", "REAL"),
            ("TakeProfit3", "REAL"),
            ("Tp1Hit", "INTEGER DEFAULT 0"),
            ("Tp2Hit", "INTEGER DEFAULT 0"),
            ("RemainingFraction", "REAL DEFAULT 1.0"),
            ("WeightedExitSum", "REAL DEFAULT 0")
        };

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragmaCommand = new SqliteCommand("PRAGMA table_info(SimulatedTrades)", connection))
        await using (var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                existingColumns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        foreach (var (name, type) in expectedColumns)
        {
            if (existingColumns.Contains(name))
                continue;

            await using var alterCommand = new SqliteCommand($"ALTER TABLE SimulatedTrades ADD COLUMN {name} {type}", connection);
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<int> AddAsync(SimulatedTrade trade, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO SimulatedTrades
            (Symbol, EntryTime, EntryPrice, TakeProfit, StopLoss, Note, Profile,
             ScoreAtEntry, Rsi, Adx, AtrPercent, EmaDistanceAtr, SwingUsageAtr,
             VolumeSpike, VolumeImbalance, RelativeStrength, RiskRewardAtEntry,
             TrendScore, StructureScore, VolumeScore, CandleScore, SetupScore,
             MomentumScore, VolatilityScore, TrendStrengthScore,
             PatternName, SmartMoneyLabel, BreakoutSource, MarketRegime, IsBullTrap, IsBearTrap,
             TakeProfit1, TakeProfit3, Tp1Hit, Tp2Hit, RemainingFraction, WeightedExitSum, Closed)
            VALUES
            (@Symbol, @EntryTime, @EntryPrice, @TakeProfit, @StopLoss, @Note, @Profile,
             @ScoreAtEntry, @Rsi, @Adx, @AtrPercent, @EmaDistanceAtr, @SwingUsageAtr,
             @VolumeSpike, @VolumeImbalance, @RelativeStrength, @RiskRewardAtEntry,
             @TrendScore, @StructureScore, @VolumeScore, @CandleScore, @SetupScore,
             @MomentumScore, @VolatilityScore, @TrendStrengthScore,
             @PatternName, @SmartMoneyLabel, @BreakoutSource, @MarketRegime, @IsBullTrap, @IsBearTrap,
             @TakeProfit1, @TakeProfit3, 0, 0, 1.0, 0, 0);
            SELECT last_insert_rowid();
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", trade.Symbol);
        command.Parameters.AddWithValue("@EntryTime", trade.EntryTime.ToString("O"));
        command.Parameters.AddWithValue("@EntryPrice", (double)trade.EntryPrice);
        command.Parameters.AddWithValue("@TakeProfit", (double)trade.TakeProfit);
        command.Parameters.AddWithValue("@StopLoss", (double)trade.StopLoss);
        command.Parameters.AddWithValue("@Note", trade.Note ?? "");
        command.Parameters.AddWithValue("@Profile", trade.Profile ?? "");
        command.Parameters.AddWithValue("@ScoreAtEntry", (double)trade.ScoreAtEntry);
        command.Parameters.AddWithValue("@Rsi", (double)trade.Rsi);
        command.Parameters.AddWithValue("@Adx", (double)trade.Adx);
        command.Parameters.AddWithValue("@AtrPercent", (double)trade.AtrPercent);
        command.Parameters.AddWithValue("@EmaDistanceAtr", (double)trade.EmaDistanceAtr);
        command.Parameters.AddWithValue("@SwingUsageAtr", (double)trade.SwingUsageAtr);
        command.Parameters.AddWithValue("@VolumeSpike", (double)trade.VolumeSpike);
        command.Parameters.AddWithValue("@VolumeImbalance", (double)trade.VolumeImbalance);
        command.Parameters.AddWithValue("@RelativeStrength", (double)trade.RelativeStrength);
        command.Parameters.AddWithValue("@RiskRewardAtEntry", (double)trade.RiskRewardAtEntry);
        command.Parameters.AddWithValue("@TrendScore", (double)trade.TrendScore);
        command.Parameters.AddWithValue("@StructureScore", (double)trade.StructureScore);
        command.Parameters.AddWithValue("@VolumeScore", (double)trade.VolumeScore);
        command.Parameters.AddWithValue("@CandleScore", (double)trade.CandleScore);
        command.Parameters.AddWithValue("@SetupScore", (double)trade.SetupScore);
        command.Parameters.AddWithValue("@MomentumScore", (double)trade.MomentumScore);
        command.Parameters.AddWithValue("@VolatilityScore", (double)trade.VolatilityScore);
        command.Parameters.AddWithValue("@TrendStrengthScore", (double)trade.TrendStrengthScore);
        command.Parameters.AddWithValue("@PatternName", trade.PatternName ?? "");
        command.Parameters.AddWithValue("@SmartMoneyLabel", trade.SmartMoneyLabel ?? "");
        command.Parameters.AddWithValue("@BreakoutSource", trade.BreakoutSource ?? "");
        command.Parameters.AddWithValue("@MarketRegime", trade.MarketRegime ?? "");
        command.Parameters.AddWithValue("@IsBullTrap", trade.IsBullTrap ? 1 : 0);
        command.Parameters.AddWithValue("@IsBearTrap", trade.IsBearTrap ? 1 : 0);
        command.Parameters.AddWithValue("@TakeProfit1", (object?)(trade.TakeProfit1.HasValue ? (double)trade.TakeProfit1.Value : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("@TakeProfit3", (object?)(trade.TakeProfit3.HasValue ? (double)trade.TakeProfit3.Value : null) ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<SimulatedTrade>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await ReadTradesAsync("SELECT * FROM SimulatedTrades ORDER BY Id DESC", cancellationToken);

    public async Task<IReadOnlyList<SimulatedTrade>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        await ReadTradesAsync("SELECT * FROM SimulatedTrades WHERE Closed = 0", cancellationToken);

    public async Task UpdateTradeDetailsAsync(int id, decimal takeProfit, decimal stopLoss, string note, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE SimulatedTrades
            SET TakeProfit = @TakeProfit, StopLoss = @StopLoss, Note = @Note
            WHERE Id = @Id AND Closed = 0
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@TakeProfit", (double)takeProfit);
        command.Parameters.AddWithValue("@StopLoss", (double)stopLoss);
        command.Parameters.AddWithValue("@Note", note ?? "");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePartialExitStateAsync(
        int id, bool tp1Hit, bool tp2Hit, decimal remainingFraction, decimal weightedExitSum, decimal stopLoss,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE SimulatedTrades
            SET Tp1Hit = @Tp1Hit, Tp2Hit = @Tp2Hit, RemainingFraction = @RemainingFraction,
                WeightedExitSum = @WeightedExitSum, StopLoss = @StopLoss
            WHERE Id = @Id AND Closed = 0
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Tp1Hit", tp1Hit ? 1 : 0);
        command.Parameters.AddWithValue("@Tp2Hit", tp2Hit ? 1 : 0);
        command.Parameters.AddWithValue("@RemainingFraction", (double)remainingFraction);
        command.Parameters.AddWithValue("@WeightedExitSum", (double)weightedExitSum);
        command.Parameters.AddWithValue("@StopLoss", (double)stopLoss);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CloseTradeAsync(int id, decimal exitPrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE SimulatedTrades
            SET Closed = 1, ExitTime = @ExitTime, ExitPrice = @ExitPrice, OutcomePercent = @OutcomePercent, ExitReason = @ExitReason
            WHERE Id = @Id
            """;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@ExitTime", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@ExitPrice", (double)exitPrice);
        command.Parameters.AddWithValue("@OutcomePercent", (double)outcomePercent);
        command.Parameters.AddWithValue("@ExitReason", exitReason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SimulatedTrade>> ReadTradesAsync(string sql, CancellationToken cancellationToken)
    {
        var result = new List<SimulatedTrade>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SimulatedTrade
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Symbol = reader.GetString(reader.GetOrdinal("Symbol")),
                EntryTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("EntryTime"))),
                EntryPrice = Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("EntryPrice"))),
                TakeProfit = Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("TakeProfit"))),
                StopLoss = Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("StopLoss"))),
                Note = GetStringOrDefault(reader, "Note"),
                Profile = GetStringOrDefault(reader, "Profile"),
                ScoreAtEntry = GetDecimalOrDefault(reader, "ScoreAtEntry"),
                Rsi = GetDecimalOrDefault(reader, "Rsi"),
                Adx = GetDecimalOrDefault(reader, "Adx"),
                AtrPercent = GetDecimalOrDefault(reader, "AtrPercent"),
                EmaDistanceAtr = GetDecimalOrDefault(reader, "EmaDistanceAtr"),
                SwingUsageAtr = GetDecimalOrDefault(reader, "SwingUsageAtr"),
                VolumeSpike = GetDecimalOrDefault(reader, "VolumeSpike"),
                VolumeImbalance = GetDecimalOrDefault(reader, "VolumeImbalance"),
                RelativeStrength = GetDecimalOrDefault(reader, "RelativeStrength"),
                RiskRewardAtEntry = GetDecimalOrDefault(reader, "RiskRewardAtEntry"),
                TrendScore = GetDecimalOrDefault(reader, "TrendScore"),
                StructureScore = GetDecimalOrDefault(reader, "StructureScore"),
                VolumeScore = GetDecimalOrDefault(reader, "VolumeScore"),
                CandleScore = GetDecimalOrDefault(reader, "CandleScore"),
                SetupScore = GetDecimalOrDefault(reader, "SetupScore"),
                MomentumScore = GetDecimalOrDefault(reader, "MomentumScore"),
                VolatilityScore = GetDecimalOrDefault(reader, "VolatilityScore"),
                TrendStrengthScore = GetDecimalOrDefault(reader, "TrendStrengthScore"),
                PatternName = GetStringOrDefault(reader, "PatternName"),
                SmartMoneyLabel = GetStringOrDefault(reader, "SmartMoneyLabel"),
                BreakoutSource = GetStringOrDefault(reader, "BreakoutSource"),
                MarketRegime = GetStringOrDefault(reader, "MarketRegime"),
                IsBullTrap = reader.GetInt32(reader.GetOrdinal("IsBullTrap")) == 1,
                IsBearTrap = reader.GetInt32(reader.GetOrdinal("IsBearTrap")) == 1,
                Closed = reader.GetInt32(reader.GetOrdinal("Closed")) == 1,
                ExitTime = reader.IsDBNull(reader.GetOrdinal("ExitTime")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("ExitTime"))),
                ExitPrice = reader.IsDBNull(reader.GetOrdinal("ExitPrice")) ? null : Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("ExitPrice"))),
                OutcomePercent = reader.IsDBNull(reader.GetOrdinal("OutcomePercent")) ? null : Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("OutcomePercent"))),
                ExitReason = GetStringOrDefault(reader, "ExitReason"),
                TakeProfit1 = GetNullableDecimal(reader, "TakeProfit1"),
                TakeProfit3 = GetNullableDecimal(reader, "TakeProfit3"),
                Tp1Hit = reader.GetInt32(reader.GetOrdinal("Tp1Hit")) == 1,
                Tp2Hit = reader.GetInt32(reader.GetOrdinal("Tp2Hit")) == 1,
                RemainingFraction = GetDecimalOrDefault(reader, "RemainingFraction", fallback: 1.0m),
                WeightedExitSum = GetDecimalOrDefault(reader, "WeightedExitSum")
            });
        }

        return result;
    }

    private static string GetStringOrDefault(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
    }

    private static decimal GetDecimalOrDefault(SqliteDataReader reader, string column, decimal fallback = 0m)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? fallback : Convert.ToDecimal(reader.GetDouble(ordinal));
    }

    private static decimal? GetNullableDecimal(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetDouble(ordinal));
    }
}