using CryptoScanner.Application.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;

namespace CryptoScanner.UI;

public partial class BacktestWindow : Window
{
    private readonly IMarketDataService _marketData;
    private readonly AssetAnalyzer _assetAnalyzer;
    private CancellationTokenSource? _cts;

    public BacktestWindow(IMarketDataService marketData, AssetAnalyzer assetAnalyzer)
    {
        InitializeComponent();

        _marketData = marketData;
        _assetAnalyzer = assetAnalyzer;

        dpEnd.SelectedDate = DateTime.Today;
        dpStart.SelectedDate = DateTime.Today.AddYears(-1);

        txtMinScore.Text = ScannerSettings.BuyOpportunityScore.ToString("F0");
        txtMinResistDistance.Text = ScannerSettings.MinResistanceDistance.ToString("F0");
        txtMinVolumeSpike.Text = ScannerSettings.MinVolumeSpike.ToString("F2");
        txtMinRiskReward.Text = ScannerSettings.MinRiskReward.ToString("F1");
        txtMinStopDistance.Text = "0"; // 0 = sem piso, reproduz o comportamento atual do app ao vivo
        txtMaxRiskReward.Text = "999"; // efetivamente sem teto

        UpdateManualSymbolsCount();
    }

    private void TxtManualSymbols_TextChanged(object sender, TextChangedEventArgs e)
    {
        // O evento pode disparar durante o InitializeComponent(), antes de
        // txtManualSymbolsCount existir — ignora nesse caso.
        if (txtManualSymbolsCount == null)
            return;

        UpdateManualSymbolsCount();
    }

    private void UpdateManualSymbolsCount()
    {
        int count = txtManualSymbols.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        txtManualSymbolsCount.Text = $"({count} moeda{(count == 1 ? "" : "s")})";
    }

    private void BtnFillYears_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(txtYearsBack.Text, out int years) || years <= 0)
        {
            MessageBox.Show("Informe um número válido de anos (ex.: 3).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var end = dpEnd.SelectedDate ?? DateTime.Today;
        dpEnd.SelectedDate = end;
        dpStart.SelectedDate = end.AddYears(-years);
    }

    private bool TryGetDateRange(out DateTime start, out DateTime end)
    {
        start = default;
        end = default;

        if (dpStart.SelectedDate is not DateTime s || dpEnd.SelectedDate is not DateTime en)
        {
            MessageBox.Show("Selecione as datas de início e fim.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (s >= en)
        {
            MessageBox.Show("A data de início deve ser anterior à data de fim.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        start = DateTime.SpecifyKind(s, DateTimeKind.Utc);
        end = DateTime.SpecifyKind(en, DateTimeKind.Utc);
        return true;
    }

    private async Task<List<string>?> ResolveSymbolsAsync()
    {
        if (rbManual.IsChecked == true)
        {
            var symbols = txtManualSymbols.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .ToList();

            if (symbols.Count == 0)
            {
                MessageBox.Show("Informe ao menos um símbolo (ex.: BTCUSDT,ETHUSDT).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            return symbols;
        }

        if (!int.TryParse(txtTopN.Text, out int topN) || topN <= 0)
        {
            MessageBox.Show("Informe um número válido de moedas (ex.: 20).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        txtStatus.Text = "Buscando lista de moedas mais líquidas...";
        var allSymbols = await _marketData.GetUsdtSymbolsAsync();
        return allSymbols.Take(topN).ToList();
    }

    private bool TryBuildThresholds(out EligibilityThresholds thresholds)
    {
        thresholds = EligibilityThresholds.Default;

        if (!decimal.TryParse(txtMinScore.Text, out decimal minScore) ||
            !decimal.TryParse(txtMinResistDistance.Text, out decimal minResistDistance) ||
            !decimal.TryParse(txtMinVolumeSpike.Text, out decimal minVolumeSpike) ||
            !decimal.TryParse(txtMinRiskReward.Text, out decimal minRiskReward) ||
            !decimal.TryParse(txtMinStopDistance.Text, out decimal minStopDistance) ||
            !decimal.TryParse(txtMaxRiskReward.Text, out decimal maxRiskReward))
        {
            MessageBox.Show("Revise os valores de limiares — todos precisam ser números válidos.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        thresholds = new EligibilityThresholds
        {
            BuyOpportunityScore = minScore,
            BearRegimePenalty = ScannerSettings.BearRegimePenalty,
            SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
            MinVolumeSpike = minVolumeSpike,
            DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
            MinResistanceDistance = minResistDistance,
            MinRiskReward = minRiskReward,
            MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
            MinStopDistancePercent = minStopDistance,
            MaxRiskReward = maxRiskReward,
            EnablePullbackBounce = chkPullbackBounce.IsChecked == true
        };

        return true;
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var thresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        SetRunningState(true);
        dgComparison.Visibility = Visibility.Collapsed;
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            var summary = await backtester.RunAsync(
                symbols,
                start,
                end,
                profile,
                thresholds,
                onProgress: (message, percent) => Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = message;
                    pbProgress.Value = percent;
                }),
                _cts.Token);

            string skippedInfo = summary.SkippedSymbols.Count > 0
                ? $"\n\n⚠ {summary.SkippedSymbols.Count} de {symbols.Count} moedas não entraram no teste:\n" +
                  string.Join("\n", summary.SkippedSymbols)
                : "";

            txtSummaryResult.Text =
                $"Operações: {summary.TotalTrades}   |   " +
                $"Win Rate: {summary.WinRate:F1}%   |   " +
                $"Retorno Acumulado: {summary.TotalReturnPercent:F2}%   |   " +
                $"Drawdown Máx.: {summary.MaxDrawdownPercent:F2}%   |   " +
                $"Profit Factor: {summary.ProfitFactor:F2}\n" +
                "(Retorno e Drawdown são somatórios percentuais por operação, não simulação de banca composta.)\n\n" +
                $"RR Médio de Entrada: {summary.AvgRiskRewardAtEntry:F2}   |   " +
                $"Win Rate de Equilíbrio: {summary.BreakEvenWinRate:F1}%   |   " +
                $"Edge: {summary.Edge:F1} pontos % ({(summary.Edge >= 0 ? "vantagem" : "desvantagem")} estatística)\n\n" +
                $"Filtros (motivos de rejeição, agregado): {summary.Diagnostics.Summary}" +
                skippedInfo;

            dgTrades.ItemsSource = summary.Trades;
            txtStatus.Text = summary.TotalTrades == 0
                ? "Nenhuma operação simulada no período/moedas/limiares selecionados."
                : "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Backtest cancelado.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao rodar o backtest.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnCompareScenarios_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var baseThresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        decimal[] riskRewardScenarios = { 1.5m, 2m, 2.5m, 3m, 3.5m };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var rr in riskRewardScenarios)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var scenarioThresholds = new EligibilityThresholds
                {
                    BuyOpportunityScore = baseThresholds.BuyOpportunityScore,
                    BearRegimePenalty = baseThresholds.BearRegimePenalty,
                    SidewaysRegimePenalty = baseThresholds.SidewaysRegimePenalty,
                    MinVolumeSpike = baseThresholds.MinVolumeSpike,
                    DefensiveMinVolumeSpike = baseThresholds.DefensiveMinVolumeSpike,
                    MinResistanceDistance = baseThresholds.MinResistanceDistance,
                    MinRiskReward = rr,
                    MinRelativeStrengthPercent = baseThresholds.MinRelativeStrengthPercent,
                    MinStopDistancePercent = baseThresholds.MinStopDistancePercent,
                    MaxRiskReward = baseThresholds.MaxRiskReward,
                    EnablePullbackBounce = baseThresholds.EnablePullbackBounce
                };

                var summary = await backtester.RunAsync(
                    symbols,
                    start,
                    end,
                    profile,
                    scenarioThresholds,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[RR≥{rr}] {message}";
                        pbProgress.Value = percent;
                    }),
                    _cts.Token);

                results.Add(new ScenarioResult
                {
                    Label = $"RR ≥ {rr}",
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    TotalReturnPercent = summary.TotalReturnPercent,
                    MaxDrawdownPercent = summary.MaxDrawdownPercent,
                    ProfitFactor = summary.ProfitFactor,
                    AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = summary.BreakEvenWinRate,
                    Edge = summary.Edge
                });
            }

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;
            txtSummaryResult.Text = "Comparação de cenários concluída — veja a tabela de resultados por Risk/Reward mínimo abaixo. " +
                                     "Os outros limiares (Score, Dist. Resist., Volume Spike) ficaram fixos nos valores informados acima.";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Comparação cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao comparar cenários.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnCompareStopScenarios_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var baseThresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        // RR fica fixo no valor da tela — só o piso de distância do stop varia,
        // pra isolar o efeito desse critério especificamente.
        decimal[] stopDistanceScenarios = { 0m, 2m, 3m, 5m, 8m };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var stopMin in stopDistanceScenarios)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var scenarioThresholds = new EligibilityThresholds
                {
                    BuyOpportunityScore = baseThresholds.BuyOpportunityScore,
                    BearRegimePenalty = baseThresholds.BearRegimePenalty,
                    SidewaysRegimePenalty = baseThresholds.SidewaysRegimePenalty,
                    MinVolumeSpike = baseThresholds.MinVolumeSpike,
                    DefensiveMinVolumeSpike = baseThresholds.DefensiveMinVolumeSpike,
                    MinResistanceDistance = baseThresholds.MinResistanceDistance,
                    MinRiskReward = baseThresholds.MinRiskReward,
                    MinRelativeStrengthPercent = baseThresholds.MinRelativeStrengthPercent,
                    MinStopDistancePercent = stopMin,
                    MaxRiskReward = baseThresholds.MaxRiskReward,
                    EnablePullbackBounce = baseThresholds.EnablePullbackBounce
                };

                var summary = await backtester.RunAsync(
                    symbols,
                    start,
                    end,
                    profile,
                    scenarioThresholds,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[Stop≥{stopMin}%] {message}";
                        pbProgress.Value = percent;
                    }),
                    _cts.Token);

                results.Add(new ScenarioResult
                {
                    Label = $"Stop ≥ {stopMin}%",
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    TotalReturnPercent = summary.TotalReturnPercent,
                    MaxDrawdownPercent = summary.MaxDrawdownPercent,
                    ProfitFactor = summary.ProfitFactor,
                    AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = summary.BreakEvenWinRate,
                    Edge = summary.Edge
                });
            }

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;
            txtSummaryResult.Text = $"Comparação por piso de Stop concluída — RR mínimo fixo em {baseThresholds.MinRiskReward}. " +
                                     "Isso isola o efeito de exigir um stop com distância mínima, independente da proporção RR.";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Comparação cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao comparar cenários.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnCompareMaxRRScenarios_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var baseThresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        // MinRiskReward fica fixo no valor da tela — só o teto varia,
        // pra isolar o efeito de excluir RRs extremos (potencialmente mal-calibrados).
        decimal[] maxRiskRewardScenarios = { 999m, 10m, 8m, 6m, 4m };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var maxRR in maxRiskRewardScenarios)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var scenarioThresholds = new EligibilityThresholds
                {
                    BuyOpportunityScore = baseThresholds.BuyOpportunityScore,
                    BearRegimePenalty = baseThresholds.BearRegimePenalty,
                    SidewaysRegimePenalty = baseThresholds.SidewaysRegimePenalty,
                    MinVolumeSpike = baseThresholds.MinVolumeSpike,
                    DefensiveMinVolumeSpike = baseThresholds.DefensiveMinVolumeSpike,
                    MinResistanceDistance = baseThresholds.MinResistanceDistance,
                    MinRiskReward = baseThresholds.MinRiskReward,
                    MinRelativeStrengthPercent = baseThresholds.MinRelativeStrengthPercent,
                    MinStopDistancePercent = baseThresholds.MinStopDistancePercent,
                    MaxRiskReward = maxRR,
                    EnablePullbackBounce = baseThresholds.EnablePullbackBounce
                };

                var summary = await backtester.RunAsync(
                    symbols,
                    start,
                    end,
                    profile,
                    scenarioThresholds,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[RR≤{maxRR}] {message}";
                        pbProgress.Value = percent;
                    }),
                    _cts.Token);

                results.Add(new ScenarioResult
                {
                    Label = maxRR >= 999m ? "Sem teto" : $"RR ≤ {maxRR}",
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    TotalReturnPercent = summary.TotalReturnPercent,
                    MaxDrawdownPercent = summary.MaxDrawdownPercent,
                    ProfitFactor = summary.ProfitFactor,
                    AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = summary.BreakEvenWinRate,
                    Edge = summary.Edge
                });
            }

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;
            txtSummaryResult.Text = $"Comparação por teto de RR concluída — RR mínimo fixo em {baseThresholds.MinRiskReward}. " +
                                     "Isso isola o efeito de excluir operações com RR muito alto (possível resistência mal-calibrada).";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Comparação cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao comparar cenários.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnCompareNewPathsScenarios_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var baseThresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        // Dois cenários: sem o caminho novo (baseline) e com o Caminho A habilitado.
        var pathScenarios = new (string Label, bool EnableA)[]
        {
            ("Baseline (sem Caminho A)", false),
            ("+ Caminho A (Repique)", true)
        };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var scenario in pathScenarios)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var scenarioThresholds = new EligibilityThresholds
                {
                    BuyOpportunityScore = baseThresholds.BuyOpportunityScore,
                    BearRegimePenalty = baseThresholds.BearRegimePenalty,
                    SidewaysRegimePenalty = baseThresholds.SidewaysRegimePenalty,
                    MinVolumeSpike = baseThresholds.MinVolumeSpike,
                    DefensiveMinVolumeSpike = baseThresholds.DefensiveMinVolumeSpike,
                    MinResistanceDistance = baseThresholds.MinResistanceDistance,
                    MinRiskReward = baseThresholds.MinRiskReward,
                    MinRelativeStrengthPercent = baseThresholds.MinRelativeStrengthPercent,
                    MinStopDistancePercent = baseThresholds.MinStopDistancePercent,
                    MaxRiskReward = baseThresholds.MaxRiskReward,
                    EnablePullbackBounce = scenario.EnableA
                };

                var summary = await backtester.RunAsync(
                    symbols,
                    start,
                    end,
                    profile,
                    scenarioThresholds,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[{scenario.Label}] {message}";
                        pbProgress.Value = percent;
                    }),
                    _cts.Token);

                results.Add(new ScenarioResult
                {
                    Label = scenario.Label,
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    TotalReturnPercent = summary.TotalReturnPercent,
                    MaxDrawdownPercent = summary.MaxDrawdownPercent,
                    ProfitFactor = summary.ProfitFactor,
                    AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = summary.BreakEvenWinRate,
                    Edge = summary.Edge
                });
            }

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;
            txtSummaryResult.Text = "Comparação concluída — compare o baseline (comportamento atual do app) " +
                                     "com a adição do Caminho A (repique dentro de tendência de alta já estabelecida).";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Comparação cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao comparar cenários.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnFreezeSymbolList_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(txtTopN.Text, out int topN) || topN <= 0)
        {
            MessageBox.Show("Informe um número válido em \"Top automático\" antes de congelar (ex.: 20).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        txtStatus.Text = "Buscando lista de moedas mais líquidas para congelar...";

        try
        {
            var allSymbols = await _marketData.GetUsdtSymbolsAsync();
            var frozenList = allSymbols.Take(topN).ToList();

            txtManualSymbols.Text = string.Join(",", frozenList);
            rbManual.IsChecked = true;

            txtStatus.Text = $"Lista congelada: {frozenList.Count} moedas copiadas para o campo Manual. " +
                              "Os próximos testes vão usar exatamente essas moedas, independente de mudanças no volume da Binance.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        txtStatus.Text = "Cancelando...";
    }

    private async void BtnComparePeriods_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(txtPeriodYears.Text, out int periodYears) || periodYears <= 0)
        {
            MessageBox.Show("Informe um número válido de anos por período (ex.: 1).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(txtPeriodCount.Text, out int periodCount) || periodCount <= 0)
        {
            MessageBox.Show("Informe uma quantidade válida de períodos (ex.: 4).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var anchorEnd = dpEnd.SelectedDate ?? DateTime.Today;
        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : ScanProfile.Swing;

        List<string>? symbols;
        try
        {
            symbols = await ResolveSymbolsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível obter a lista de moedas.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
            return;
        }

        if (symbols == null)
            return;

        // Ignora os campos da tela — força a configuração padrão exata do app ao vivo.
        var defaultThresholds = EligibilityThresholds.Default;

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        var allTrades = new List<BacktestTradeResult>();
        var aggregatedDiagnostics = new FilterDiagnostics();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            for (int i = periodCount; i >= 1; i--)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var periodEnd = DateTime.SpecifyKind(anchorEnd.AddYears(-(i - 1) * periodYears), DateTimeKind.Utc);
                var periodStart = DateTime.SpecifyKind(anchorEnd.AddYears(-i * periodYears), DateTimeKind.Utc);

                string label = $"{periodStart:dd/MM/yy} – {periodEnd:dd/MM/yy}";

                var summary = await backtester.RunAsync(
                    symbols,
                    periodStart,
                    periodEnd,
                    profile,
                    defaultThresholds,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[{label}] {message}";
                        pbProgress.Value = percent;
                    }),
                    _cts.Token);

                results.Add(new ScenarioResult
                {
                    Label = label,
                    TotalTrades = summary.TotalTrades,
                    WinRate = summary.WinRate,
                    TotalReturnPercent = summary.TotalReturnPercent,
                    MaxDrawdownPercent = summary.MaxDrawdownPercent,
                    ProfitFactor = summary.ProfitFactor,
                    AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = summary.BreakEvenWinRate,
                    Edge = summary.Edge
                });

                allTrades.AddRange(summary.Trades);
                StrategyBacktester.MergeDiagnostics(aggregatedDiagnostics, summary.Diagnostics);
            }

            // Junta todos os trades de todos os períodos numa amostra só — mais poder estatístico
            // do que qualquer período isolado.
            var pooledSummary = StrategyBacktester.BuildSummary(allTrades, aggregatedDiagnostics, new List<string>());
            results.Add(new ScenarioResult
            {
                Label = "TOTAL (todos os períodos juntos)",
                TotalTrades = pooledSummary.TotalTrades,
                WinRate = pooledSummary.WinRate,
                TotalReturnPercent = pooledSummary.TotalReturnPercent,
                MaxDrawdownPercent = pooledSummary.MaxDrawdownPercent,
                ProfitFactor = pooledSummary.ProfitFactor,
                AvgRiskRewardAtEntry = pooledSummary.AvgRiskRewardAtEntry,
                BreakEvenWinRate = pooledSummary.BreakEvenWinRate,
                Edge = pooledSummary.Edge
            });

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;
            dgTrades.ItemsSource = allTrades.OrderBy(t => t.ExitTime).ToList();

            txtSummaryResult.Text =
                $"Teste definitivo: configuração PADRÃO do scanner ao vivo (Score≥{ScannerSettings.BuyOpportunityScore:F0}, " +
                $"RR≥{ScannerSettings.MinRiskReward:F1} sem teto, sem caminhos alternativos), testada em {periodCount} período(s) " +
                $"de {periodYears} ano(s) cada, não sobrepostos, com {symbols.Count} moedas. " +
                "A linha \"TOTAL\" junta todas as operações de todos os períodos numa amostra só, pra dar mais poder estatístico à conclusão " +
                "do que qualquer período isolado.";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Teste cancelado.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao rodar o teste definitivo.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private void SetRunningState(bool isRunning)
    {
        btnRun.IsEnabled = !isRunning;
        btnCompare.IsEnabled = !isRunning;
        btnCompareStop.IsEnabled = !isRunning;
        btnCompareMaxRR.IsEnabled = !isRunning;
        btnCompareNewPaths.IsEnabled = !isRunning;
        btnComparePeriods.IsEnabled = !isRunning;
        btnCancel.IsEnabled = isRunning;
        pbProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        if (!isRunning)
            _cts = null;
    }
}

/// <summary>
/// Colore o valor de Edge (WinRate real - WinRate de equilíbrio): verde quando
/// positivo (vantagem estatística), vermelho quando negativo (desvantagem).
/// </summary>
public sealed class EdgeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double edge)
            return edge >= 0 ? Brushes.DarkGreen : Brushes.DarkRed;

        return Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}