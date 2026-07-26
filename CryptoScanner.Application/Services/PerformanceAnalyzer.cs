using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoScanner.Application.Services;

public static class PerformanceAnalyzer
{
    public static PerformanceReport Analyze(IReadOnlyList<SignalHistory> allSignals)
    {
        var evaluated = allSignals.Where(s => s.Evaluated && s.OutcomePercent.HasValue).ToList();

        return new PerformanceReport
        {
            TotalEvaluated = evaluated.Count,
            ByScore = BucketByRange(evaluated, s => s.FinalScore, ScoreRanges),
            ByRsi = BucketByRange(evaluated, s => s.Rsi, RsiRanges),
            ByAdx = BucketByRange(evaluated, s => s.Adx, AdxRanges),
            ByAtrPercent = BucketByRange(evaluated, s => s.AtrPercent, AtrRanges),
            ByPattern = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.PatternName) ? "(nenhum)" : s.PatternName),
            BySmartMoney = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.SmartMoneyLabel) ? "(nenhum)" : s.SmartMoneyLabel),
            ByBreakoutSource = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.BreakoutSource) ? "(nenhum)" : s.BreakoutSource),
            ByMarketRegime = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.MarketRegime) ? "(desconhecido)" : s.MarketRegime),
            ByProfile = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.Profile) ? "(legado)" : s.Profile),
            ByExitReason = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.ExitReason) ? "(desconhecido)" : s.ExitReason)
        };
    }

    private static readonly (string Label, decimal Min, decimal Max)[] RsiRanges =
    {
        ("< 30", decimal.MinValue, 30m),
        ("30 - 45", 30m, 45m),
        ("45 - 55", 45m, 55m),
        ("55 - 70", 55m, 70m),
        ("> 70", 70m, decimal.MaxValue)
    };

    private static readonly (string Label, decimal Min, decimal Max)[] AdxRanges =
    {
        ("< 20 (fraca)", decimal.MinValue, 20m),
        ("20 - 40 (moderada)", 20m, 40m),
        ("> 40 (forte)", 40m, decimal.MaxValue)
    };

    private static readonly (string Label, decimal Min, decimal Max)[] ScoreRanges =
    {
        ("< 40", decimal.MinValue, 40m),
        ("40 - 55", 40m, 55m),
        ("55 - 70", 55m, 70m),
        ("70 - 85", 70m, 85m),
        (">= 85", 85m, decimal.MaxValue)
    };

    private static readonly (string Label, decimal Min, decimal Max)[] AtrRanges =
    {
        ("< 1%", decimal.MinValue, 1m),
        ("1% - 2%", 1m, 2m),
        ("2% - 4%", 2m, 4m),
        ("> 4%", 4m, decimal.MaxValue)
    };

    private static List<PerformanceBucket> BucketByRange(
        List<SignalHistory> signals,
        Func<SignalHistory, decimal> selector,
        (string Label, decimal Min, decimal Max)[] ranges)
    {
        var result = new List<PerformanceBucket>();

        foreach (var range in ranges)
        {
            var group = signals.Where(s =>
            {
                decimal value = selector(s);
                return value >= range.Min && value < range.Max;
            }).ToList();

            if (group.Count == 0)
                continue;

            result.Add(BuildBucket(range.Label, group));
        }

        return result;
    }

    private static List<PerformanceBucket> BucketByCategory(
        List<SignalHistory> signals,
        Func<SignalHistory, string> selector)
    {
        return signals
            .GroupBy(selector)
            .Select(group => BuildBucket(group.Key, group.ToList()))
            .OrderByDescending(b => b.Count)
            .ToList();
    }

    private static PerformanceBucket BuildBucket(string label, List<SignalHistory> group)
    {
        int wins = group.Count(s => s.OutcomePercent > 0);
        double winRate = group.Count > 0 ? wins * 100.0 / group.Count : 0;
        double avgReturn = group.Count > 0 ? (double)group.Average(s => s.OutcomePercent ?? 0) : 0;

        return new PerformanceBucket
        {
            Label = label,
            Count = group.Count,
            WinRate = winRate,
            AvgReturn = avgReturn
        };
    }
}