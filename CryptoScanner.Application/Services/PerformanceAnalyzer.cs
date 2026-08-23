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
            ByScore = BucketByRange(evaluated, s => s.FinalScore, s => s.OutcomePercent, ScoreRanges),
            ByRsi = BucketByRange(evaluated, s => s.Rsi, s => s.OutcomePercent, RsiRanges),
            ByAdx = BucketByRange(evaluated, s => s.Adx, s => s.OutcomePercent, AdxRanges),
            ByAtrPercent = BucketByRange(evaluated, s => s.AtrPercent, s => s.OutcomePercent, AtrRanges),
            ByPattern = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.PatternName) ? "(nenhum)" : s.PatternName, s => s.OutcomePercent),
            BySmartMoney = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.SmartMoneyLabel) ? "(nenhum)" : s.SmartMoneyLabel, s => s.OutcomePercent),
            ByBreakoutSource = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.BreakoutSource) ? "(nenhum)" : s.BreakoutSource, s => s.OutcomePercent),
            ByMarketRegime = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.MarketRegime) ? "(desconhecido)" : s.MarketRegime, s => s.OutcomePercent),
            ByProfile = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.Profile) ? "(legado)" : s.Profile, s => s.OutcomePercent),
            ByExitReason = BucketByCategory(evaluated, s => string.IsNullOrEmpty(s.ExitReason) ? "(desconhecido)" : s.ExitReason, s => s.OutcomePercent)
        };
    }

    /// <summary>
    /// Fase 3 do roadmap (Aprendizado), Passo 2 — mesma lógica de bucket do Analyze() acima,
    /// mas rodando sobre trades do Backtest em vez de sinais reais ao vivo. Existe porque
    /// SignalHistory (sinais reais) é uma amostra pequena e lenta de crescer (cada trade Swing
    /// leva até 10 dias pra resolver); o Backtest já gera centenas de trades por teste — é
    /// onde está o poder estatístico de verdade pra responder "qual RSI/ADX/Score funciona
    /// melhor?" (ver investigação de 16-20/08/2026 que descobriu o padrão de RSI via CSV/Python
    /// manual — isso aqui automatiza esse mesmo processo).
    /// IMPORTANTE (lição da própria investigação): a resposta que esse relatório dá depende
    /// muito de os limiares do teste terem sido SOLTOS (exploração) ou VALIDADOS/restritivos
    /// (produção) — limiares restritivos já pré-filtram as variáveis que você quer estudar,
    /// escondendo o padrão. Pra descobrir fator novo, rode um Backtest com limiares soltos
    /// antes de analisar aqui.
    /// </summary>
    public static BacktestPerformanceReport AnalyzeBacktestTrades(IReadOnlyList<BacktestTradeResult> trades)
    {
        var list = trades.ToList(); // trades de Backtest já são todos "avaliados" por definição — é retrospectivo

        return new BacktestPerformanceReport
        {
            TotalTrades = list.Count,
            ByScore = BucketByRange(list, t => t.Score, t => t.OutcomePercent, ScoreRanges),
            ByRsi = BucketByRange(list, t => t.Rsi, t => t.OutcomePercent, RsiRanges),
            ByAdx = BucketByRange(list, t => t.Adx, t => t.OutcomePercent, AdxRanges),
            ByAtrPercent = BucketByRange(list, t => t.AtrPercent, t => t.OutcomePercent, AtrRanges),
            ByRiskReward = BucketByRange(list, t => t.RiskRewardAtEntry, t => t.OutcomePercent, RiskRewardRanges),
            ByPattern = BucketByCategory(list, t => string.IsNullOrEmpty(t.PatternName) ? "(nenhum)" : t.PatternName, t => t.OutcomePercent),
            BySmartMoney = BucketByCategory(list, t => string.IsNullOrEmpty(t.SmartMoneyLabel) ? "(nenhum)" : t.SmartMoneyLabel, t => t.OutcomePercent),
            ByBreakoutSource = BucketByCategory(list, t => string.IsNullOrEmpty(t.BreakoutSource) ? "(nenhum)" : t.BreakoutSource, t => t.OutcomePercent),
            ByMarketRegime = BucketByCategory(list, t => string.IsNullOrEmpty(t.MarketRegime) ? "(desconhecido)" : t.MarketRegime, t => t.OutcomePercent),
            ByDirection = BucketByCategory(list, t => t.Direction.ToString(), t => t.OutcomePercent),
            ByExitReason = BucketByCategory(list, t => string.IsNullOrEmpty(t.ExitReason) ? "(desconhecido)" : t.ExitReason, t => t.OutcomePercent)
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
        ("< 15", decimal.MinValue, 15m),
        ("15 - 20", 15m, 20m),
        ("20 - 25", 20m, 25m),
        ("25 - 40", 25m, 40m),
        (">= 40", 40m, decimal.MaxValue)
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

    // Faixas de Risk/Reward na entrada — só faz sentido pro Backtest (SignalHistory não
    // guarda RR de entrada de forma consistente o suficiente pra bucket). Ranges escolhidos
    // pra bater com a exploração manual que já fizemos via CSV/Python nessa investigação.
    private static readonly (string Label, decimal Min, decimal Max)[] RiskRewardRanges =
    {
        ("0,5 - 1", 0.5m, 1m),
        ("1 - 1,5", 1m, 1.5m),
        ("1,5 - 2", 1.5m, 2m),
        ("2 - 3", 2m, 3m),
        ("3 - 5", 3m, 5m),
        (">= 5", 5m, decimal.MaxValue)
    };

    private static List<PerformanceBucket> BucketByRange<T>(
        List<T> items,
        Func<T, decimal> selector,
        Func<T, decimal?> outcomeSelector,
        (string Label, decimal Min, decimal Max)[] ranges)
    {
        var result = new List<PerformanceBucket>();

        foreach (var range in ranges)
        {
            var group = items.Where(i =>
            {
                decimal value = selector(i);
                return value >= range.Min && value < range.Max;
            }).ToList();

            if (group.Count == 0)
                continue;

            result.Add(BuildBucket(range.Label, group, outcomeSelector));
        }

        return result;
    }

    private static List<PerformanceBucket> BucketByCategory<T>(
        List<T> items,
        Func<T, string> selector,
        Func<T, decimal?> outcomeSelector)
    {
        return items
            .GroupBy(selector)
            .Select(group => BuildBucket(group.Key, group.ToList(), outcomeSelector))
            .OrderByDescending(b => b.Count)
            .ToList();
    }

    private static PerformanceBucket BuildBucket<T>(string label, List<T> group, Func<T, decimal?> outcomeSelector)
    {
        int wins = group.Count(i => (outcomeSelector(i) ?? 0) > 0);
        double winRate = group.Count > 0 ? wins * 100.0 / group.Count : 0;
        double avgReturn = group.Count > 0 ? (double)group.Average(i => outcomeSelector(i) ?? 0) : 0;

        return new PerformanceBucket
        {
            Label = label,
            Count = group.Count,
            WinRate = winRate,
            AvgReturn = avgReturn
        };
    }
}