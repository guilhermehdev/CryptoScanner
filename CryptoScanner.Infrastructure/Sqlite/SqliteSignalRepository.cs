using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteSignalRepository : ISignalRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeGate=new(1,1);
    private bool _initialized;

    public SqliteSignalRepository(string databasePath) => _connectionString = $"Data Source={databasePath}";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializeGate.WaitAsync(cancellationToken);
        try{ if(_initialized)return;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            CREATE TABLE IF NOT EXISTS ScanRuns(Id TEXT PRIMARY KEY, Profile TEXT NOT NULL, CompletedUtc TEXT NOT NULL, DiagnosticsJson TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS TechnicalSetupAlerts
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CandleOpenUtc TEXT NOT NULL,
                EntryUtc TEXT,
                RecordedUtc TEXT NOT NULL,
                Symbol TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Setup TEXT NOT NULL,
                Price REAL NOT NULL,
                Score REAL NOT NULL,
                Profile TEXT NOT NULL,
                MarketRegime TEXT NOT NULL,
                ReturnAfter1HourPercent REAL,
                ReturnAfter6HoursPercent REAL,
                ReturnAfter24HoursPercent REAL,
                MaximumFavorable24HoursPercent REAL,
                MaximumAdverse24HoursPercent REAL,
                UNIQUE(Symbol, Direction, Setup, Profile, CandleOpenUtc)
            );
            CREATE INDEX IF NOT EXISTS IX_TechnicalSetupAlerts_RecordedUtc ON TechnicalSetupAlerts (RecordedUtc DESC);
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
                IsBearTrap INTEGER DEFAULT 0,
                Direction TEXT NOT NULL DEFAULT 'Long'
            );
            CREATE INDEX IF NOT EXISTS IX_Signals_Evaluated_Timestamp ON Signals (Evaluated, Timestamp);
            CREATE INDEX IF NOT EXISTS IX_Signals_Symbol_Timestamp ON Signals (Symbol, Timestamp);
            """;
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migração leve para bancos criados antes desta mudança.
        var newColumns = new[]
        {
            "ExecutionJson TEXT", "TakeProfit REAL", "StopLoss REAL", "ExitReason TEXT", "Profile TEXT", "MarketRegime TEXT",
            "Rsi REAL", "Adx REAL", "AtrPercent REAL", "EmaDistanceAtr REAL", "SwingUsageAtr REAL",
            "VolumeSpike REAL", "VolumeImbalance REAL", "RelativeStrength REAL", "RiskReward REAL",
            "TrendScore INTEGER", "StructureScore INTEGER", "VolumeScore INTEGER", "CandleScore INTEGER",
            "SetupScore INTEGER", "MomentumScore INTEGER", "VolatilityScore INTEGER", "TrendStrengthScore INTEGER",
            "PatternName TEXT", "SmartMoneyLabel TEXT", "BreakoutSource TEXT",
            "IsBullTrap INTEGER DEFAULT 0", "IsBearTrap INTEGER DEFAULT 0", "Direction TEXT NOT NULL DEFAULT 'Long'"
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
        var technicalAlertColumns = new[]
        {
            "EntryUtc TEXT", "ReturnAfter1HourPercent REAL", "ReturnAfter6HoursPercent REAL",
            "ReturnAfter24HoursPercent REAL", "MaximumFavorable24HoursPercent REAL", "MaximumAdverse24HoursPercent REAL"
        };
        foreach (var column in technicalAlertColumns)
        {
            try
            {
                await using var alter = new SqliteCommand($"ALTER TABLE TechnicalSetupAlerts ADD COLUMN {column}", connection);
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException)
            {
                // Coluna já existe ou a tabela foi criada com ela.
            }
        }
        _initialized=true;
        }finally{_initializeGate.Release();}
    }

    public async Task<bool> TryInsertSignalAsync(SignalSnapshot snapshot, int windowDays, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync("""
            INSERT INTO Signals
            (ExecutionJson, Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, PreviousScore, Evaluated,
             TakeProfit, StopLoss, ExitReason, Profile, MarketRegime,
             Rsi, Adx, AtrPercent, EmaDistanceAtr, SwingUsageAtr, VolumeSpike, VolumeImbalance, RelativeStrength, RiskReward,
             TrendScore, StructureScore, VolumeScore, CandleScore, SetupScore, MomentumScore, VolatilityScore, TrendStrengthScore,
             PatternName, SmartMoneyLabel, BreakoutSource, IsBullTrap, IsBearTrap, Direction)
            SELECT
            @ExecutionJson, @Timestamp, @Symbol, @Price, @Score, @Signal, NULL, NULL, @PreviousScore, 0,
             @TakeProfit, @StopLoss, NULL, @Profile, @MarketRegime,
             @Rsi, @Adx, @AtrPercent, @EmaDistanceAtr, @SwingUsageAtr, @VolumeSpike, @VolumeImbalance, @RelativeStrength, @RiskReward,
             @TrendScore, @StructureScore, @VolumeScore, @CandleScore, @SetupScore, @MomentumScore, @VolatilityScore, @TrendStrengthScore,
             @PatternName, @SmartMoneyLabel, @BreakoutSource, @IsBullTrap, @IsBearTrap, @Direction
            WHERE NOT EXISTS (SELECT 1 FROM Signals WHERE Symbol=@Symbol AND Profile=@Profile AND BreakoutSource=@BreakoutSource AND Direction=@Direction AND Timestamp>=@WindowStart)
            """, cancellationToken,
            ("@ExecutionJson",snapshot.ExecutionJson),
            ("@WindowStart", DateTime.UtcNow.AddDays(-windowDays).ToString("O")),
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
            ("@IsBearTrap", snapshot.IsBearTrap ? 1 : 0),
            ("@Direction", snapshot.Direction.ToString())) > 0;
    }

    public async Task<bool> SignalExistsWithinWindowAsync(string symbol, string signal, string profile, int windowDays, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM Signals WHERE Symbol = @Symbol AND Signal = @Signal AND Profile = @Profile AND Timestamp >= @WindowStart";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Symbol", symbol);
        command.Parameters.AddWithValue("@Signal", signal);
        command.Parameters.AddWithValue("@Profile", profile);
        command.Parameters.AddWithValue("@WindowStart", DateTime.UtcNow.AddDays(-windowDays).ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private const string SelectColumns = """
        Id, Timestamp, Symbol, Price, FinalScore, Signal, OutcomePrice, OutcomePercent, Evaluated, PreviousScore,
        TakeProfit, StopLoss, ExitReason, Profile, MarketRegime,
        Rsi, Adx, AtrPercent, EmaDistanceAtr, SwingUsageAtr, VolumeSpike, VolumeImbalance, RelativeStrength, RiskReward,
        TrendScore, StructureScore, VolumeScore, CandleScore, SetupScore, MomentumScore, VolatilityScore, TrendStrengthScore,
        PatternName, SmartMoneyLabel, BreakoutSource, IsBullTrap, IsBearTrap, Direction, ExecutionJson
        """;

    public Task<IReadOnlyList<SignalHistory>> GetSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync($"SELECT {SelectColumns} FROM Signals ORDER BY Id DESC", cancellationToken);

    public Task<IReadOnlyList<SignalHistory>> GetPendingSignalsAsync(CancellationToken cancellationToken = default) =>
        ReadSignalsAsync($"SELECT {SelectColumns} FROM Signals WHERE Evaluated = 0", cancellationToken);

    public Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE Signals SET OutcomePrice = @OutcomePrice, OutcomePercent = @OutcomePercent, Evaluated = 1, ExitReason = @ExitReason WHERE Id = @Id", cancellationToken,
            ("@Id", id), ("@OutcomePrice", (double)outcomePrice), ("@OutcomePercent", (double)outcomePercent), ("@ExitReason", exitReason));

    public async Task<IReadOnlyList<TechnicalSetupAlert>> SaveTechnicalSetupAlertsAsync(IReadOnlyList<TechnicalSetupAlert> alerts, CancellationToken cancellationToken = default)
    {
        if (alerts.Count == 0)
            return Array.Empty<TechnicalSetupAlert>();

        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO TechnicalSetupAlerts
            (CandleOpenUtc, EntryUtc, RecordedUtc, Symbol, Direction, Setup, Price, Score, Profile, MarketRegime)
            VALUES (@CandleOpenUtc, @EntryUtc, @RecordedUtc, @Symbol, @Direction, @Setup, @Price, @Score, @Profile, @MarketRegime)
            """;

        var candleOpenUtc = command.CreateParameter(); candleOpenUtc.ParameterName = "@CandleOpenUtc"; command.Parameters.Add(candleOpenUtc);
        var entryUtc = command.CreateParameter(); entryUtc.ParameterName = "@EntryUtc"; command.Parameters.Add(entryUtc);
        var recordedUtc = command.CreateParameter(); recordedUtc.ParameterName = "@RecordedUtc"; command.Parameters.Add(recordedUtc);
        var symbol = command.CreateParameter(); symbol.ParameterName = "@Symbol"; command.Parameters.Add(symbol);
        var direction = command.CreateParameter(); direction.ParameterName = "@Direction"; command.Parameters.Add(direction);
        var setup = command.CreateParameter(); setup.ParameterName = "@Setup"; command.Parameters.Add(setup);
        var price = command.CreateParameter(); price.ParameterName = "@Price"; command.Parameters.Add(price);
        var score = command.CreateParameter(); score.ParameterName = "@Score"; command.Parameters.Add(score);
        var profile = command.CreateParameter(); profile.ParameterName = "@Profile"; command.Parameters.Add(profile);
        var marketRegime = command.CreateParameter(); marketRegime.ParameterName = "@MarketRegime"; command.Parameters.Add(marketRegime);

        var inserted = new List<TechnicalSetupAlert>();
        foreach (var alert in alerts)
        {
            candleOpenUtc.Value = alert.CandleOpenUtc.ToUniversalTime().ToString("O");
            entryUtc.Value = alert.EntryUtc.ToUniversalTime().ToString("O");
            recordedUtc.Value = alert.RecordedUtc.ToUniversalTime().ToString("O");
            symbol.Value = alert.Symbol;
            direction.Value = alert.Direction.ToString();
            setup.Value = alert.Setup;
            price.Value = (double)alert.Price;
            score.Value = (double)alert.Score;
            profile.Value = alert.Profile;
            marketRegime.Value = alert.MarketRegime;
            if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
                inserted.Add(alert);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<IReadOnlyList<TechnicalSetupAlert>> GetTechnicalSetupAlertsAsync(int limit = 500, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("""
            SELECT Id, CandleOpenUtc, COALESCE(EntryUtc, CandleOpenUtc), RecordedUtc, Symbol, Direction, Setup, Price, Score, Profile, MarketRegime,
                   ReturnAfter1HourPercent, ReturnAfter6HoursPercent, ReturnAfter24HoursPercent, MaximumFavorable24HoursPercent, MaximumAdverse24HoursPercent
            FROM TechnicalSetupAlerts ORDER BY CandleOpenUtc DESC, Id DESC LIMIT @Limit
            """, connection);
        command.Parameters.AddWithValue("@Limit", Math.Clamp(limit, 1, 5_000));
        return await ReadTechnicalSetupAlertsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TechnicalSetupAlert>> GetTechnicalSetupAlertsDueForEvaluationAsync(DateTime dueBeforeUtc, int limit = 25, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand("""
            SELECT Id, CandleOpenUtc, COALESCE(EntryUtc, CandleOpenUtc), RecordedUtc, Symbol, Direction, Setup, Price, Score, Profile, MarketRegime,
                   ReturnAfter1HourPercent, ReturnAfter6HoursPercent, ReturnAfter24HoursPercent, MaximumFavorable24HoursPercent, MaximumAdverse24HoursPercent
            FROM TechnicalSetupAlerts
            WHERE ReturnAfter24HoursPercent IS NULL AND COALESCE(EntryUtc, CandleOpenUtc) <= @DueBefore
            ORDER BY COALESCE(EntryUtc, CandleOpenUtc) ASC, Id ASC LIMIT @Limit
            """, connection);
        command.Parameters.AddWithValue("@DueBefore", dueBeforeUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@Limit", Math.Clamp(limit, 1, 200));
        return await ReadTechnicalSetupAlertsAsync(command, cancellationToken);
    }

    public Task UpdateTechnicalSetupAlertOutcomeAsync(int id, decimal returnAfter1HourPercent, decimal returnAfter6HoursPercent, decimal returnAfter24HoursPercent, decimal maximumFavorable24HoursPercent, decimal maximumAdverse24HoursPercent, CancellationToken cancellationToken = default) =>
        ExecuteAsync("""
            UPDATE TechnicalSetupAlerts SET ReturnAfter1HourPercent=@Return1, ReturnAfter6HoursPercent=@Return6,
            ReturnAfter24HoursPercent=@Return24, MaximumFavorable24HoursPercent=@MaximumFavorable,
            MaximumAdverse24HoursPercent=@MaximumAdverse WHERE Id=@Id
            """, cancellationToken, ("@Id", id), ("@Return1", (double)returnAfter1HourPercent), ("@Return6", (double)returnAfter6HoursPercent),
            ("@Return24", (double)returnAfter24HoursPercent), ("@MaximumFavorable", (double)maximumFavorable24HoursPercent), ("@MaximumAdverse", (double)maximumAdverse24HoursPercent));

    private static async Task<IReadOnlyList<TechnicalSetupAlert>> ReadTechnicalSetupAlertsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var alerts = new List<TechnicalSetupAlert>();
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(new TechnicalSetupAlert
            {
                Id = reader.GetInt32(0),
                CandleOpenUtc = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                EntryUtc = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                RecordedUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Symbol = reader.GetString(4),
                Direction = Enum.TryParse<TradeDirection>(reader.GetString(5), true, out var direction) ? direction : TradeDirection.Long,
                Setup = reader.GetString(6), Price = Convert.ToDecimal(reader.GetDouble(7)), Score = Convert.ToDecimal(reader.GetDouble(8)),
                Profile = reader.GetString(9), MarketRegime = reader.GetString(10),
                ReturnAfter1HourPercent = reader.IsDBNull(11) ? null : Convert.ToDecimal(reader.GetDouble(11)),
                ReturnAfter6HoursPercent = reader.IsDBNull(12) ? null : Convert.ToDecimal(reader.GetDouble(12)),
                ReturnAfter24HoursPercent = reader.IsDBNull(13) ? null : Convert.ToDecimal(reader.GetDouble(13)),
                MaximumFavorable24HoursPercent = reader.IsDBNull(14) ? null : Convert.ToDecimal(reader.GetDouble(14)),
                MaximumAdverse24HoursPercent = reader.IsDBNull(15) ? null : Convert.ToDecimal(reader.GetDouble(15))
            });
        }
        return alerts;
    }

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
                ExecutionJson = reader.IsDBNull(reader.GetOrdinal("ExecutionJson")) ? "" : reader.GetString(reader.GetOrdinal("ExecutionJson")),
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
                IsBearTrap = !reader.IsDBNull(36) && reader.GetInt32(36) == 1,
                Direction = reader.IsDBNull(37) ? TradeDirection.Long : Enum.TryParse<TradeDirection>(reader.GetString(37), true, out var direction) ? direction : TradeDirection.Long
            });
        }
        return signals;
    }

    public Task UpdateExecutionAsync(int id,string expectedJson,LabTrade trade,CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE Signals SET ExecutionJson=@json,Evaluated=@closed,OutcomePrice=@price,OutcomePercent=@result,ExitReason=@reason WHERE Id=@id AND ExecutionJson=@expected",cancellationToken,
            ("@id",id),("@expected",expectedJson),("@json",System.Text.Json.JsonSerializer.Serialize(trade)),("@closed",trade.Closed?1:0),
            ("@price",(double)trade.LastPrice),("@result",trade.Closed?(object)(double)(trade.NetProfit/trade.Cost*100):DBNull.Value),("@reason",trade.Closed?trade.ExitReason:""));

    public Task SaveScanRunAsync(string profile, FilterDiagnostics diagnostics, CancellationToken cancellationToken = default) =>
        ExecuteAsync("INSERT INTO ScanRuns VALUES(@id,@profile,@at,@json)", cancellationToken,
            ("@id",diagnostics.RunId),("@profile",profile),("@at",diagnostics.CompletedUtc.ToString("O")),
            ("@json",System.Text.Json.JsonSerializer.Serialize(diagnostics)));

    private async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqliteCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
