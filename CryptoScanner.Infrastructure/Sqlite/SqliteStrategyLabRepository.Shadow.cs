using System.Text.Json;
using CryptoScanner.Core.Models;
using Microsoft.Data.Sqlite;
namespace CryptoScanner.Infrastructure.Sqlite;

public sealed partial class SqliteStrategyLabRepository
{
    private static async Task<List<LabTrade>> ShadowTrades(SqliteConnection db, SqliteTransaction? tx, bool history, CancellationToken token)
    {
        await using var cmd=db.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText=history?"SELECT StateJson FROM LabShadowTrades ORDER BY Id DESC LIMIT 200":"SELECT StateJson FROM LabShadowTrades WHERE Closed=0";
        var result=new List<LabTrade>(); await using var r=await cmd.ExecuteReaderAsync(token);
        while(await r.ReadAsync(token)) result.Add(JsonSerializer.Deserialize<LabTrade>(r.GetString(0))!);
        return result;
    }

    private static async Task ObserveShadow(SqliteConnection db, SqliteTransaction tx, LabOpportunity opportunity,
        LabParameters parameters, long opportunityId, string reason, CancellationToken token)
    {
        if(reason is not ("Limite de posições" or "Capital indisponível")) return;
        // Bypass portfolio capacity only; quote, signal and price-level validations still apply.
        var (trade,_) = LabSimulation.TryOpen(opportunity,parameters,LabParameters.Ticket,0,false);
        if(trade is null) return;
        // Avoid treating overlapping observations of the same asset as independent experiments.
        long existing=Convert.ToInt64(await Sql(db,tx,"SELECT COUNT(*) FROM LabShadowTrades WHERE VariantId=$v AND Symbol=$s AND Closed=0",token,
            ("$v",parameters.Id),("$s",opportunity.Symbol)));
        if(existing>0) return;
        trade.Id=Convert.ToInt64(await Sql(db,tx,"""
            INSERT INTO LabShadowTrades(VariantId,OpportunityId,Symbol,StateJson,Closed,TriggerReason)
            VALUES($v,$o,$s,'{}',0,$reason); SELECT last_insert_rowid();
            """,token,("$v",parameters.Id),("$o",opportunityId),("$s",opportunity.Symbol),("$reason",reason)));
        await Sql(db,tx,"UPDATE LabShadowTrades SET StateJson=$json WHERE Id=$id",token,
            ("$json",JsonSerializer.Serialize(trade)),("$id",trade.Id));
    }

    private static async Task TickShadows(SqliteConnection db,SqliteTransaction tx,IReadOnlyDictionary<string,decimal> prices,long at,CancellationToken token)
    {
        foreach(var trade in await ShadowTrades(db,tx,false,token))
        {
            if(!prices.TryGetValue(trade.Symbol,out var price)||price<=0||at<=trade.LastQuoteMs) continue;
            var exits=LabSimulation.Tick(trade,price,at);
            await Sql(db,tx,"UPDATE LabShadowTrades SET StateJson=$json,Closed=$closed,NetProfit=$profit WHERE Id=$id",token,
                ("$json",JsonSerializer.Serialize(trade)),("$closed",trade.Closed?1:0),("$profit",trade.Closed?(object)(double)trade.NetProfit:null),("$id",trade.Id));
            foreach(var exit in exits)
                await Sql(db,tx,"INSERT INTO LabShadowExits(TradeId,AtMs,EventJson) VALUES($id,$at,$json)",token,
                    ("$id",trade.Id),("$at",at),("$json",JsonSerializer.Serialize(exit)));
        }
    }
}
