using System.Text.Json;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace CryptoScanner.Infrastructure.Sqlite;

public sealed class SqliteStrategyLabRepository(string databasePath) : IStrategyLabRepository
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly string _connectionString=new SqliteConnectionStringBuilder{DataSource=databasePath,DefaultTimeout=30}.ToString();
    private bool _initialized;
    private static async Task<object?> Sql(SqliteConnection db,SqliteTransaction? tx,string sql,CancellationToken token,params (string,object?)[] args)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (name,value) in args)cmd.Parameters.AddWithValue(name,value??DBNull.Value);
        return await cmd.ExecuteScalarAsync(token);
    }
    private async Task<T> Use<T>(Func<SqliteConnection,Task<T>> work,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await using var db=new SqliteConnection(_connectionString);await db.OpenAsync(token);
            if(!_initialized)
            {
                using var tx=db.BeginTransaction();
                await Sql(db,tx,"""
                    CREATE TABLE IF NOT EXISTS LabSettings(Id INTEGER PRIMARY KEY,Enabled INTEGER NOT NULL);
                    INSERT OR IGNORE INTO LabSettings VALUES(1,1);
                    CREATE TABLE IF NOT EXISTS LabVariants(Id INTEGER PRIMARY KEY,ParametersJson TEXT NOT NULL,
                        Cash REAL NOT NULL,PeakEquity REAL NOT NULL,MaxDrawdown REAL NOT NULL,EngineVersion TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS LabOpportunities(Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Symbol TEXT NOT NULL,Profile TEXT NOT NULL,Bucket INTEGER NOT NULL,GridTimeMs INTEGER NOT NULL,
                        QuoteTimeMs INTEGER NOT NULL,Quote REAL,FeaturesJson TEXT NOT NULL,
                        UNIQUE(Symbol,Profile,Bucket));
                    CREATE TABLE IF NOT EXISTS LabDecisions(OpportunityId INTEGER NOT NULL,VariantId INTEGER NOT NULL,
                        AtMs INTEGER NOT NULL,Accepted INTEGER NOT NULL,Reason TEXT NOT NULL,
                        PRIMARY KEY(OpportunityId,VariantId));
                    CREATE TABLE IF NOT EXISTS LabTrades(Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        VariantId INTEGER NOT NULL,OpportunityId INTEGER NOT NULL,Symbol TEXT NOT NULL,
                        StateJson TEXT NOT NULL,Closed INTEGER NOT NULL,NetProfit REAL,
                        UNIQUE(VariantId,OpportunityId));
                    CREATE INDEX IF NOT EXISTS IX_LabOpen ON LabTrades(Closed,Symbol);
                    CREATE TABLE IF NOT EXISTS LabExits(Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TradeId INTEGER NOT NULL,AtMs INTEGER NOT NULL,EventJson TEXT NOT NULL);
                    """,token);
                foreach(var p in LabParameters.Initial)
                    await Sql(db,tx,"INSERT OR IGNORE INTO LabVariants VALUES($id,$json,10000,10000,0,'lab-v1');",token,
                        ("$id",p.Id),("$json",JsonSerializer.Serialize(p)));
                tx.Commit();_initialized=true;
            }
            return await work(db);
        }
        finally{_gate.Release();}
    }
    private static async Task<List<(LabParameters Parameters,decimal Cash,decimal Peak,decimal Drawdown)>> Variants(SqliteConnection db,SqliteTransaction? tx,CancellationToken token)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT ParametersJson,Cash,PeakEquity,MaxDrawdown FROM LabVariants ORDER BY Id";
        var result=new List<(LabParameters,decimal,decimal,decimal)>();
        await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token))result.Add((JsonSerializer.Deserialize<LabParameters>(r.GetString(0))!,Convert.ToDecimal(r.GetDouble(1)),Convert.ToDecimal(r.GetDouble(2)),Convert.ToDecimal(r.GetDouble(3))));
        return result;
    }
    private static async Task<List<LabTrade>> Trades(SqliteConnection db,SqliteTransaction? tx,bool all,CancellationToken token)
    {
        await using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=all?"SELECT StateJson FROM LabTrades ORDER BY Id DESC LIMIT 200":"SELECT StateJson FROM LabTrades WHERE Closed=0";
        var result=new List<LabTrade>();await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token))result.Add(JsonSerializer.Deserialize<LabTrade>(r.GetString(0))!);
        return result;
    }
    private static async Task UpdateEquity(SqliteConnection db,SqliteTransaction tx,CancellationToken token)
    {
        var open=await Trades(db,tx,false,token);
        foreach(var v in await Variants(db,tx,token))
        {
            decimal equity=v.Cash+open.Where(t=>t.VariantId==v.Parameters.Id).Sum(t=>t.LiquidationValue);
            decimal peak=Math.Max(v.Peak,equity),dd=Math.Max(v.Drawdown,(peak-equity)/peak*100);
            await Sql(db,tx,"UPDATE LabVariants SET PeakEquity=$peak,MaxDrawdown=$dd WHERE Id=$id",token,
                ("$peak",(double)peak),("$dd",(double)dd),("$id",v.Parameters.Id));
        }
    }
    public Task<IReadOnlySet<string>> ObservedSymbolsAsync(string profile,long bucket,CancellationToken token=default)=>Use<IReadOnlySet<string>>(async db=>
    {
        await using var cmd=db.CreateCommand();cmd.CommandText="SELECT Symbol FROM LabOpportunities WHERE Profile=$profile AND Bucket=$bucket";
        cmd.Parameters.AddWithValue("$profile",profile);cmd.Parameters.AddWithValue("$bucket",bucket);
        var symbols=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token))symbols.Add(r.GetString(0));return symbols;
    },token);
    public Task ObserveAsync(LabOpportunity o,CancellationToken token=default)=>Use(async db=>
    {
        o=o with {Symbol=o.Symbol.ToUpperInvariant()};
        using var tx=db.BeginTransaction();
        long inserted=Convert.ToInt64(await Sql(db,tx,"""
            INSERT OR IGNORE INTO LabOpportunities(Symbol,Profile,Bucket,GridTimeMs,QuoteTimeMs,Quote,FeaturesJson)
            VALUES($symbol,$profile,$bucket,$grid,$at,$price,$json);SELECT changes();
            """,token,("$symbol",o.Symbol),("$profile",o.Profile),("$bucket",o.GridTimeMs/300000),
            ("$grid",o.GridTimeMs),("$at",o.QuoteTimeMs),("$price",o.Quote.HasValue?(object)(double)o.Quote.Value:null),("$json",o.FeaturesJson)));
        if(inserted==0){tx.Commit();return 0;}
        long id=Convert.ToInt64(await Sql(db,tx,"SELECT last_insert_rowid()",token));
        bool enabled=Convert.ToInt64(await Sql(db,tx,"SELECT Enabled FROM LabSettings WHERE Id=1",token))==1;
        var open=await Trades(db,tx,false,token);
        foreach(var v in await Variants(db,tx,token))
        {
            var (trade,reason)=enabled?LabSimulation.TryOpen(o,v.Parameters,v.Cash,open.Count(t=>t.VariantId==v.Parameters.Id),
                open.Any(t=>t.VariantId==v.Parameters.Id&&t.Symbol==o.Symbol)):(null,"Novas entradas pausadas");
            await Sql(db,tx,"INSERT INTO LabDecisions VALUES($o,$v,$at,$accepted,$reason)",token,
                ("$o",id),("$v",v.Parameters.Id),("$at",o.DecisionTimeMs),("$accepted",trade is null?0:1),("$reason",reason));
            if(trade is null)continue;
            trade.Id=Convert.ToInt64(await Sql(db,tx,"INSERT INTO LabTrades(VariantId,OpportunityId,Symbol,StateJson,Closed) VALUES($v,$o,$s,'{}',0);SELECT last_insert_rowid();",token,
                ("$v",v.Parameters.Id),("$o",id),("$s",o.Symbol)));
            await Sql(db,tx,"UPDATE LabTrades SET StateJson=$json WHERE Id=$id;UPDATE LabVariants SET Cash=Cash-$cost WHERE Id=$v;",token,
                ("$json",JsonSerializer.Serialize(trade)),("$id",trade.Id),("$v",v.Parameters.Id),("$cost",(double)trade.Cost));
        }
        await UpdateEquity(db,tx,token);tx.Commit();return 0;
    },token);
    public Task<IReadOnlyList<string>> OpenSymbolsAsync(CancellationToken token=default)=>Use<IReadOnlyList<string>>(async db=>
        (await Trades(db,null,false,token)).Select(t=>t.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),token);
    public Task TickAsync(IReadOnlyDictionary<string,decimal> prices,long at,CancellationToken token=default)=>Use(async db=>
    {
        using var tx=db.BeginTransaction();
        foreach(var t in await Trades(db,tx,false,token))
        {
            if(!prices.TryGetValue(t.Symbol,out decimal price)||price<=0||at<=t.LastQuoteMs)continue;
            var exits=LabSimulation.Tick(t,price,at);
            await Sql(db,tx,"UPDATE LabTrades SET StateJson=$json,Closed=$closed,NetProfit=$profit WHERE Id=$id",token,
                ("$json",JsonSerializer.Serialize(t)),("$closed",t.Closed?1:0),("$profit",t.Closed?(object)(double)t.NetProfit:null),("$id",t.Id));
            foreach(var exit in exits)
                await Sql(db,tx,"INSERT INTO LabExits(TradeId,AtMs,EventJson) VALUES($id,$at,$json)",token,
                    ("$id",t.Id),("$at",at),("$json",JsonSerializer.Serialize(exit)));
            await Sql(db,tx,"UPDATE LabVariants SET Cash=Cash+$proceeds WHERE Id=$id",token,
                ("$proceeds",(double)exits.Sum(e=>e.Proceeds)),("$id",t.VariantId));
        }
        await UpdateEquity(db,tx,token);tx.Commit();return 0;
    },token);
    public Task SetEnabledAsync(bool enabled,CancellationToken token=default)=>Use(async db=>
    {await Sql(db,null,"UPDATE LabSettings SET Enabled=$enabled WHERE Id=1",token,("$enabled",enabled?1:0));return 0;},token);
    public Task<LabReport> ReportAsync(CancellationToken token=default)=>Use(async db=>
    {
        using var tx=db.BeginTransaction(deferred:true);var open=await Trades(db,tx,false,token);var reports=new List<LabVariantReport>();
        foreach(var v in await Variants(db,tx,token))
        {
            long closed=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabTrades WHERE VariantId=$v AND Closed=1",token,("$v",v.Parameters.Id)));
            long wins=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabTrades WHERE VariantId=$v AND Closed=1 AND NetProfit>0",token,("$v",v.Parameters.Id)));
            long rejected=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabDecisions WHERE VariantId=$v AND Accepted=0",token,("$v",v.Parameters.Id)));
            long gaps=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabTrades WHERE VariantId=$v AND json_extract(StateJson,'$.HasObservationGap')=1",token,("$v",v.Parameters.Id)));
            var positions=open.Where(t=>t.VariantId==v.Parameters.Id).ToArray();
            reports.Add(new($"V{v.Parameters.Id} · {v.Parameters.Name}",v.Parameters.Mutation,v.Cash,v.Cash+positions.Sum(t=>t.LiquidationValue),v.Drawdown,positions.Length,closed,wins,rejected,gaps));
        }
        bool enabled=Convert.ToInt64(await Sql(db,tx,"SELECT Enabled FROM LabSettings WHERE Id=1",token))==1;
        long count=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabOpportunities",token));
        var history=await Trades(db,tx,true,token);
        var decisions=new List<LabDecisionRow>();
        await using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="SELECT o.Symbol,o.Profile,d.VariantId,d.AtMs,d.Accepted,d.Reason,o.FeaturesJson FROM LabDecisions d JOIN LabOpportunities o ON o.Id=d.OpportunityId ORDER BY d.AtMs DESC,d.OpportunityId DESC,d.VariantId LIMIT 200";
            await using var r=await cmd.ExecuteReaderAsync(token);
            while(await r.ReadAsync(token))
            {
                var a=JsonSerializer.Deserialize<AssetScore>(r.GetString(6))!;
                decisions.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt64(3),r.GetInt64(4)==1,r.GetString(5),
                    $"Indicadores congelados do grid: score {a.Score:F2} | pressão {a.BuyingPressureScore?.ToString("F2")??"—"} | RSI {a.Rsi:F1} | volume {a.VolumeSpike:F2}× | regime {a.MarketRegime}\n"+
                    $"Preço no grid {a.Close:0.########} | suporte {a.Support:0.########} | resistência {a.Resistance:0.########} | {a.BuyingPressureDetails}"));
            }
        }
        tx.Commit();return new LabReport(enabled,reports,history,count,decisions);
    },token);
}
