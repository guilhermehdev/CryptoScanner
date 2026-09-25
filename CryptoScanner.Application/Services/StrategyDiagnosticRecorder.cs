using CryptoScanner.Core.Models;
using CryptoScanner.Core.Models.Analysis;
using CryptoScanner.Core.Configuration;
namespace CryptoScanner.Application.Services;

public static class StrategyDiagnosticRecorder
{
    public static void RecordSingleFilterCounterfactuals(FilterDiagnostics diagnostics, EligibilityEvaluator.EligibilityResult result)
    {
        RecordSingleFilterCounterfactuals(diagnostics,FailedFilterNames(result));
    }

    public static void Record(FilterDiagnostics diagnostics, AssetAnalysis asset, EligibilityEvaluator.EligibilityResult result, DateTime at)
    {
        string key=asset.EntryStrategy.ToString();
        if(!diagnostics.Strategies.TryGetValue(key,out var bucket)) diagnostics.Strategies[key]=bucket=new();
        bucket.Evaluated++;
        if(asset.Setup.IsBreakout) diagnostics.BreakoutTriggers++;
        if(asset.Setup.IsPullbackBounce) diagnostics.PullbackTriggers++;
        var failures=FailedFilterNames(result);
        // Mede o gargalo removendo exatamente um filtro por vez. É apenas
        // diagnóstico: a elegibilidade real continua usando todos os filtros.
        RecordSingleFilterCounterfactuals(diagnostics,failures);
        // Count filter combinations only among actual triggers, not all candles.
        if(result.FailedBreakout) return;
        bucket.Triggered++;
        string targetPosition=asset.Direction == TradeDirection.Short
            ? "Não aplicável (Short)"
            : asset.Risk.TargetZone?.PositionOf(asset.Trend.Close) ?? "Sem zona registrada";
        bucket.TargetPositions[targetPosition]=bucket.TargetPositions.GetValueOrDefault(targetPosition)+1;
        if(failures.Length==0){bucket.Eligible++;return;}
        foreach(var name in failures) bucket.Rejections[name]=bucket.Rejections.GetValueOrDefault(name)+1;
        if(failures.Length==1) bucket.SoleBlocker[failures[0]]=bucket.SoleBlocker.GetValueOrDefault(failures[0])+1;
        if(bucket.Samples.Count<30) bucket.Samples.Add(new(asset.Symbol,at,asset.Trend.Close,asset.Risk.Support,asset.Risk.Resistance,asset.Risk.RiskReward,failures){TargetZone=asset.Direction == TradeDirection.Short ? null : asset.Risk.TargetZone});
    }

    private static string[] FailedFilterNames(EligibilityEvaluator.EligibilityResult result) =>
        typeof(EligibilityEvaluator.EligibilityResult).GetProperties()
            .Where(p=>p.Name.StartsWith("Failed") && (bool)p.GetValue(result)!).Select(p=>p.Name).ToArray();

    private static void RecordSingleFilterCounterfactuals(FilterDiagnostics diagnostics, string[] failures)
    {
        if(failures.Length==1)
            diagnostics.PassesRemovingOneFilter[failures[0]]=diagnostics.PassesRemovingOneFilter.GetValueOrDefault(failures[0])+1;
    }
}
