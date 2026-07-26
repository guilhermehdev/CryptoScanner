using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using System.Collections.Generic;
using System.Windows;

namespace CryptoScanner.UI;

public partial class AnalyticsWindow : Window
{
    public AnalyticsWindow(IReadOnlyList<SignalHistory> history)
    {
        InitializeComponent();

        var report = PerformanceAnalyzer.Analyze(history);

        txtSummary.Text = report.TotalEvaluated == 0
            ? "Nenhum sinal avaliado ainda. Os relatórios abaixo ficam vazios até que operações sejam concluídas (TP, SL ou timeout)."
            : $"{report.TotalEvaluated} sinais avaliados no total.";

        dgScore.ItemsSource = report.ByScore;
        dgRsi.ItemsSource = report.ByRsi;
        dgAdx.ItemsSource = report.ByAdx;
        dgAtr.ItemsSource = report.ByAtrPercent;
        dgPattern.ItemsSource = report.ByPattern;
        dgSmartMoney.ItemsSource = report.BySmartMoney;
        dgBreakoutSource.ItemsSource = report.ByBreakoutSource;
        dgRegime.ItemsSource = report.ByMarketRegime;
        dgProfile.ItemsSource = report.ByProfile;
        dgExitReason.ItemsSource = report.ByExitReason;
    }
}
