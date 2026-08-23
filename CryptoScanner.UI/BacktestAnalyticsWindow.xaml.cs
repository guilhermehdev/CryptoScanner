using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using System.Collections.Generic;
using System.Windows;

namespace CryptoScanner.UI;

public partial class BacktestAnalyticsWindow : Window
{
    public BacktestAnalyticsWindow(IReadOnlyList<BacktestTradeResult> trades)
    {
        InitializeComponent();

        var report = PerformanceAnalyzer.AnalyzeBacktestTrades(trades);

        txtSummary.Text = report.TotalTrades == 0
            ? "Nenhuma operação nesse teste — rode um Backtest antes de analisar."
            : $"{report.TotalTrades} operações analisadas.";

        dgScore.ItemsSource = report.ByScore;
        dgRsi.ItemsSource = report.ByRsi;
        dgAdx.ItemsSource = report.ByAdx;
        dgAtr.ItemsSource = report.ByAtrPercent;
        dgRiskReward.ItemsSource = report.ByRiskReward;
        dgPattern.ItemsSource = report.ByPattern;
        dgSmartMoney.ItemsSource = report.BySmartMoney;
        dgBreakoutSource.ItemsSource = report.ByBreakoutSource;
        dgRegime.ItemsSource = report.ByMarketRegime;
        dgDirection.ItemsSource = report.ByDirection;
        dgExitReason.ItemsSource = report.ByExitReason;
    }
}