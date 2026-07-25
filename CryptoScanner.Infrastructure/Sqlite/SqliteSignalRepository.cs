using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteSignalRepository : ISignalRepository
{
    private readonly string _connectionString;

    public SqliteSignalRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
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
                PreviousScore REAL,
                Evaluated INTEGER DEFAULT 0,
                TakeProfit REAL,
                StopLoss REAL,
                ExitReason TEXT,
                Profile TEXT,
                MarketRegime TEXT,
                Rsi REAL,
                Adx REAL,
                AtrPercent REAL,
                EmaDistanceAtr REAL,
                SwingUsageAtr REAL,
                VolumeSpike REAL,
                VolumeImbalance REAL,
                RelativeStrength REAL,
                RiskReward REAL,
                TrendScore INTEGER,
                StructureScore INTEGER,
                VolumeScore INTEGER,
                CandleScore INTEGER,
                SetupScore INTEGER,
                MomentumScore INTEGER,
                VolatilityScore INTEGER,
                TrendStrengthScore INTEGER,
                PatternName TEXT,
                SmartMoneyLabel TEXT,
                BreakoutSource TEXT,
                IsBullTrap INTEGER DEFAULT 0,
                IsBearTrap INTEGER DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Signals_Evaluated_Timestamp ON Signals (Evaluated, Timestamp);
            CREATE INDEX IF NOT EXISTS IX_Signals_Symbol_Timestamp ON Signals (Symbol, Timestamp);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migração leve para bancos criados antes desta mudança.
        var newColumns = new[]
        {
            "TakeProfit REAL", "StopLoss REAL", "ExitReason TEXT", "Profile TEXT", "MarketRegime TEXT",
            "Rsi REAL", "Adx REAL", "AtrPercent REAL", "EmaDistanceAtr REAL", "SwingUsageAtr REAL",
            "VolumeSpike REAL", "VolumeImbalance REAL", "RelativeStrength REAL", "RiskReward REAL",
            "TrendScore INTEGER", "StructureScore INTEGER", "VolumeScore INTEGER", "CandleScore INTEGER",
            "SetupScore INTEGER", "MomentumScore INTEGER", "VolatilityScore INTEGER", "TrendStrengthScore INTEGER",
            "PatternName TEXT", "SmartMoneyLabel TEXT", "BreakoutSource TEXT",
            "IsBullTrap INTEGER DEFAULT 0", "IsBearTrap INTEGER DEFAULT 0"
        };

        foreach (var column in newColumns)
        {
            try
            {
                await using var alter = new SqliteCommand($"ALTER TABLE Signals ADD COLUMN {column}", connection);
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException)
            {
                // Coluna já existe — ignora.
            }
        }
    }

    public async Task InsertSignalAsync(SignalSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync("""
            INSERT INTO Signals
            (Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, PreviousScore, Evaluated,
             TakeProfit, StopLoss, ExitReason, Profile, MarketRegime,
             Rsi, Adx, AtrPercent, EmaDistanceAtr, SwingUsageAtr, VolumeSpike, VolumeImbalance, RelativeStrength, RiskReward,
             TrendScore, StructureScore, VolumeScore, CandleScore, SetupScore, MomentumScore, VolatilityScore, TrendStrengthScore,
             PatternName, SmartMoneyLabel, BreakoutSource, IsBullTrap, IsBearTrap)
            VALUES
            (@Timestamp, @Symbol, @Price, @Score, @Signal, NULL, NULL, @PreviousScore, 0,
             @TakeProfit, @StopLoss, NULL, @Profile, @MarketRegime,
             @Rsi, @Adx, @AtrPercent, @EmaDistanceAtr, @SwingUsageAtr, @VolumeSpike, @VolumeImbalance, @RelativeStrength, @RiskReward,
             @TrendScore, @StructureScore, @VolumeScore, @CandleScore, @SetupScore, @MomentumScore, @VolatilityScore, @TrendStrengthScore,
             @PatternName, @SmartMoneyLabel, @BreakoutSource, @IsBullTrap, @IsBearTrap)
            """, cancellationToken,
            ("@Timestamp", DateTime.UtcNow.ToString("O")),
            ("@Symbol", snapshot.Symbol),
            ("@Price", (double)snapshot.Price),
            ("@Score", (double)snapshot.Score),
            ("@Signal", snapshot.Signal),
            ("@PreviousScore", (double)snapshot.PreviousScore),
            ("@TakeProfit", (double)snapshot.TakeProfit),
            ("@StopLoss", (double)snapshot.StopLoss),
            ("@Profile", snapshot.Profile),
            ("@MarketRegime", snapshot.MarketRegime),
            ("@Rsi", (double)snapshot.Rsi),
            ("@Adx", (double)snapshot.Adx),
            ("@AtrPercent", (double)snapshot.AtrPercent),
            ("@EmaDistanceAtr", (double)snapshot.EmaDistanceAtr),
            ("@SwingUsageAtr", (double)snapshot.SwingUsageAtr),
            ("@VolumeSpike", (double)snapshot.VolumeSpike),
            ("@VolumeImbalance", (double)snapshot.VolumeImbalance),
            ("@RelativeStrength", (double)snapshot.RelativeStrength),
            ("@RiskReward", (double)snapshot.RiskReward),
            ("@TrendScore", snapshot.TrendScore),
            ("@StructureScore", snapshot.StructureScore),
            ("@VolumeScore", snapshot.VolumeScore),
            ("@CandleScore", snapshot.CandleScore),
            ("@SetupScore", snapshot.SetupScore),
            ("@MomentumScore", snapshot.MomentumScore),
            ("@VolatilityScore", snapshot.VolatilityScore),
            ("@TrendStrengthScore", snapshot.TrendStrengthScore),
            ("@PatternName", snapshot.PatternName),
            ("@SmartMoneyLabel", snapshot.SmartMoneyLabel),
            ("@BreakoutSource", snapshot.BreakoutSource),
            ("@IsBullTrap", snapshot.IsBullTrap ? 1 : 0),
            ("@IsBearTrap", snapshot.IsBearTrap ? 1 : 0));
    }

    public async Task<bool> SignalExistsWithinWindowAsync(string symbol, string signal, int windowDays, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM Signals WHERE Symbol = @Symbol AND Signal = @Signal AND Timestamp >= @WindowStart";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@Signal", signal);
        command.Parameters.AddWithValue("@WindowStart", DateTime.UtcNow.AddDays(-windowDays).ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private const string SelectColumns = """
        Id, Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, Evaluated, PreviousScore,
        TakeProfit, StopLoss, ExitReason, Profile, MarketRegime,
        Rsi, Adx, AtrPercent, EmaDistanceAtr, SwingUsageAtr, VolumeSpike, VolumeImbalance, RelativeStrength, RiskReward,
        TrendScore, StructureScore, VolumeScore, CandleScore, SetupScore, MomentumScore, VolatilityScore, TrendStrengthScore,
        PatternName, SmartMoneyLabel, BreakoutSource, IsBullTrap, IsBearTrap
        """;

    public Task<IReadOnlyList<SignalHistory>> GetSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync($"SELECT {SelectColumns} FROM Signals ORDER BY Id DESC", cancellationToken);

    public Task<IReadOnlyList<SignalHistory>> GetPendingSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync($"SELECT {SelectColumns} FROM Signals WHERE Evaluated = 0", cancellationToken);

    public Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE Signals SET OutcomePrice = @OutcomePrice, OutcomePercent = @OutcomePercent, Evaluated = 1, ExitReason = @ExitReason WHERE Id = @Id", cancellationToken,
            ("@Id", id), ("@OutcomePrice", (double)outcomePrice), ("@OutcomePercent", (double)outcomePercent), ("@ExitReason", exitReason));

    public async Task<double> GetWinRateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*), COALESCE(SUM(CASE WHEN OutcomePercent > 0 THEN 1 ELSE 0 END), 0) FROM Signals WHERE Evaluated = 1";
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        long total = reader.GetInt64(0);
        return total == 0 ? 0 : reader.GetInt64(1) * 100d / total;
    }

    public async Task<double> GetAverageReturnAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("SELECT AVG(OutcomePercent) FROM Signals WHERE Evaluated = 1", connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToDouble(value);
    }

    private async Task<IReadOnlyList<SignalHistory>> ReadSignalsAsync(string sql, CancellationToken cancellationToken)
    {
        var signals = new List<SignalHistory>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            signals.Add(new SignalHistory
            {
                Id = reader.GetInt32(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                Symbol = reader.GetString(2),
                Price = Convert.ToDecimal(reader.GetDouble(3)),
                FinalScore = Convert.ToDecimal(reader.GetDouble(4)),
                Signal = reader.GetString(5),
                OutcomePrice = reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetDouble(6)),
                OutcomePercent = reader.IsDBNull(7) ? null : Convert.ToDecimal(reader.GetDouble(7)),
                Evaluated = !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
                PreviousScore = reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetDouble(9)),
                TakeProfit = reader.IsDBNull(10) ? 0 : Convert.ToDecimal(reader.GetDouble(10)),
                StopLoss = reader.IsDBNull(11) ? 0 : Convert.ToDecimal(reader.GetDouble(11)),
                ExitReason = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Profile = reader.IsDBNull(13) ? "" : reader.GetString(13),
                MarketRegime = reader.IsDBNull(14) ? "" : reader.GetString(14),
                Rsi = reader.IsDBNull(15) ? 0 : Convert.ToDecimal(reader.GetDouble(15)),
                Adx = reader.IsDBNull(16) ? 0 : Convert.ToDecimal(reader.GetDouble(16)),
                AtrPercent = reader.IsDBNull(17) ? 0 : Convert.ToDecimal(reader.GetDouble(17)),
                EmaDistanceAtr = reader.IsDBNull(18) ? 0 : Convert.ToDecimal(reader.GetDouble(18)),
                SwingUsageAtr = reader.IsDBNull(19) ? 0 : Convert.ToDecimal(reader.GetDouble(19)),
                VolumeSpike = reader.IsDBNull(20) ? 0 : Convert.ToDecimal(reader.GetDouble(20)),
                VolumeImbalance = reader.IsDBNull(21) ? 0 : Convert.ToDecimal(reader.GetDouble(21)),
                RelativeStrength = reader.IsDBNull(22) ? 0 : Convert.ToDecimal(reader.GetDouble(22)),
                RiskReward = reader.IsDBNull(23) ? 0 : Convert.ToDecimal(reader.GetDouble(23)),
                TrendScore = reader.IsDBNull(24) ? 0 : reader.GetInt32(24),
                StructureScore = reader.IsDBNull(25) ? 0 : reader.GetInt32(25),
                VolumeScore = reader.IsDBNull(26) ? 0 : reader.GetInt32(26),
                CandleScore = reader.IsDBNull(27) ? 0 : reader.GetInt32(27),
                SetupScore = reader.IsDBNull(28) ? 0 : reader.GetInt32(28),
                MomentumScore = reader.IsDBNull(29) ? 0 : reader.GetInt32(29),
                VolatilityScore = reader.IsDBNull(30) ? 0 : reader.GetInt32(30),
                TrendStrengthScore = reader.IsDBNull(31) ? 0 : reader.GetInt32(31),
                PatternName = reader.IsDBNull(32) ? "" : reader.GetString(32),
                SmartMoneyLabel = reader.IsDBNull(33) ? "" : reader.GetString(33),
                BreakoutSource = reader.IsDBNull(34) ? "" : reader.GetString(34),
                IsBullTrap = !reader.IsDBNull(35) && reader.GetInt32(35) == 1,
                IsBearTrap = !reader.IsDBNull(36) && reader.GetInt32(36) == 1
            });
        }
        return signals;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}