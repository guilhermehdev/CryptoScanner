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
using MessageBox = System.Windows.MessageBox;

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
            MaxRiskReward = maxRiskReward
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

            txtSummaryResult.Text =
                $"Operações: {summary.TotalTrades}   |   " +
                $"Win Rate: {summary.WinRate:F1}%   |   " +
                $"Retorno Acumulado: {summary.TotalReturnPercent:F2}%   |   " +
                $"Drawdown Máx.: {summary.MaxDrawdownPercent:F2}%   |   " +
                $"Profit Factor: {summary.ProfitFactor:F2}\n" +
                "(Retorno e Drawdown são somatórios percentuais por operação, não simulação de banca composta.)\n\n" +
                $"Filtros (motivos de rejeição, agregado): {summary.Diagnostics.Summary}";

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
                    MaxRiskReward = baseThresholds.MaxRiskReward
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
                    ProfitFactor = summary.ProfitFactor
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
                    MaxRiskReward = baseThresholds.MaxRiskReward
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
                    ProfitFactor = summary.ProfitFactor
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
                    MaxRiskReward = maxRR
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
                    ProfitFactor = summary.ProfitFactor
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

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        txtStatus.Text = "Cancelando...";
    }

    private void SetRunningState(bool isRunning)
    {
        btnRun.IsEnabled = !isRunning;
        btnCompare.IsEnabled = !isRunning;
        btnCompareStop.IsEnabled = !isRunning;
        btnCompareMaxRR.IsEnabled = !isRunning;
        btnCancel.IsEnabled = isRunning;
        pbProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        if (!isRunning)
            _cts = null;
    }
}
