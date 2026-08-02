using CryptoScanner.Application.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Utilities;
using CryptoScanner.Infrastructure.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class BacktestWindow : Window
{
    private readonly IMarketDataService _marketData;
    private readonly AssetAnalyzer _assetAnalyzer;
    private readonly IBacktestSettingsRepository _settingsRepository;
    private readonly IBacktestRunResultRepository _runResultRepository;
    private CancellationTokenSource? _cts;
    private List<BacktestTradeResult> _lastDisplayedTrades = new();

    public BacktestWindow(IMarketDataService marketData, AssetAnalyzer assetAnalyzer, string databasePath)
    {
        InitializeComponent();

        _marketData = marketData;
        _assetAnalyzer = assetAnalyzer;
        _settingsRepository = new SqliteBacktestSettingsRepository(databasePath);
        _runResultRepository = new SqliteBacktestRunResultRepository(databasePath);

        dpEnd.SelectedDate = DateTime.Today;
        dpStart.SelectedDate = DateTime.Today.AddYears(-1);

        txtMinScore.Text = ScannerSettings.BuyOpportunityScore.ToString("F0");
        txtMinResistDistance.Text = ScannerSettings.MinResistanceDistance.ToString("F0");
        txtMinResistDistanceAtr.Text = "10"; // provisório — a comparar empiricamente
        txtMinVolumeSpike.Text = ScannerSettings.MinVolumeSpike.ToString("F2");
        txtMinRiskReward.Text = ScannerSettings.MinRiskReward.ToString("F1");
        txtMinStopDistance.Text = "0"; // 0 = sem piso, reproduz o comportamento atual do app ao vivo
        txtMaxRiskReward.Text = "999"; // efetivamente sem teto

        UpdateManualSymbolsCount();
        Loaded += BacktestWindow_Loaded;
    }

    private async void BacktestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _settingsRepository.InitializeAsync();
            var saved = await _settingsRepository.GetManualSymbolListAsync();

            if (!string.IsNullOrWhiteSpace(saved))
            {
                txtManualSymbols.Text = saved;
                rbManual.IsChecked = true;
            }
        }
        catch
        {
            // Se falhar ao carregar, só mantém o valor padrão do campo — não bloqueia a janela.
        }
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

    private void BtnBacktestHistory_Click(object sender, RoutedEventArgs e)
    {
        var window = new BacktestHistoryWindow(_runResultRepository)
        {
            Owner = this
        };
        window.Show();
    }

    private static string ComputeSignature(
        IReadOnlyList<string> symbols, DateTime start, DateTime end, ScanProfile profile,
        EligibilityThresholds thresholds, RiskCalculationMode riskMode, int? evaluationHoursOverride)
    {
        var sb = new StringBuilder();
        sb.Append(profile.Name).Append('|');
        sb.Append(riskMode).Append('|');
        sb.Append(start.ToString("O")).Append('|');
        sb.Append(end.ToString("O")).Append('|');
        sb.Append(string.Join(",", symbols.OrderBy(s => s, StringComparer.Ordinal))).Append('|');
        sb.Append(thresholds.BuyOpportunityScore).Append('|');
        sb.Append(thresholds.MinResistanceDistance).Append('|');
        sb.Append(thresholds.MinResistanceDistanceAtrMode).Append('|');
        sb.Append(thresholds.MinVolumeSpike).Append('|');
        sb.Append(thresholds.MinRiskReward).Append('|');
        sb.Append(thresholds.MinStopDistancePercent).Append('|');
        sb.Append(thresholds.MaxRiskReward).Append('|');
        sb.Append(thresholds.EnablePullbackBounce).Append('|');
        sb.Append(evaluationHoursOverride?.ToString() ?? "default");

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private async Task SaveRunResultAsync(
        string label, IReadOnlyList<string> symbols, DateTime start, DateTime end, ScanProfile profile,
        EligibilityThresholds thresholds, RiskCalculationMode riskMode, int? evaluationHoursOverride,
        BacktestSummary summary)
    {
        try
        {
            string signature = ComputeSignature(symbols, start, end, profile, thresholds, riskMode, evaluationHoursOverride);

            await _runResultRepository.InitializeAsync();
            if (await _runResultRepository.ExistsAsync(signature))
                return; // já existe um teste idêntico salvo — não duplica

            var record = new BacktestRunResult
            {
                SignatureHash = signature,
                SavedAt = DateTime.UtcNow,
                Label = label,
                Profile = profile.Name,
                RiskMode = riskMode.ToString(),
                StartDate = start,
                EndDate = end,
                Symbols = string.Join(",", symbols),
                SymbolCount = symbols.Count,
                MinScore = thresholds.BuyOpportunityScore,
                MinResistanceDistanceSwing = thresholds.MinResistanceDistance,
                MinResistanceDistanceAtr = thresholds.MinResistanceDistanceAtrMode,
                MinVolumeSpike = thresholds.MinVolumeSpike,
                MinRiskReward = thresholds.MinRiskReward,
                MinStopDistancePercent = thresholds.MinStopDistancePercent,
                MaxRiskReward = thresholds.MaxRiskReward,
                EnablePullbackBounce = thresholds.EnablePullbackBounce,
                EvaluationHoursOverride = evaluationHoursOverride,
                TotalTrades = summary.TotalTrades,
                WinRate = summary.WinRate,
                TotalReturnPercent = summary.TotalReturnPercent,
                MaxDrawdownPercent = summary.MaxDrawdownPercent,
                ProfitFactor = summary.ProfitFactor,
                AvgRiskRewardAtEntry = summary.AvgRiskRewardAtEntry,
                BreakEvenWinRate = summary.BreakEvenWinRate,
                Edge = summary.Edge
            };

            await _runResultRepository.SaveAsync(record);
        }
        catch
        {
            // Falha ao salvar o histórico nunca deve travar ou invalidar o teste em si.
        }
    }

    private RiskCalculationMode GetSelectedRiskMode()
    {
        if (rbRiskAtr.IsChecked == true) return RiskCalculationMode.AtrBased;
        if (rbRiskSwingBuffer.IsChecked == true) return RiskCalculationMode.SwingWithAtrBuffer;
        return RiskCalculationMode.SwingBased;
    }

    private EligibilityThresholds BuildDefaultThresholdsWithCurrentAtrDistance()
    {
        if (!decimal.TryParse(txtMinResistDistanceAtr.Text, out decimal atrDistance))
            atrDistance = EligibilityThresholds.Default.MinResistanceDistanceAtrMode;

        return new EligibilityThresholds
        {
            BuyOpportunityScore = EligibilityThresholds.Default.BuyOpportunityScore,
            BearRegimePenalty = EligibilityThresholds.Default.BearRegimePenalty,
            SidewaysRegimePenalty = EligibilityThresholds.Default.SidewaysRegimePenalty,
            MinVolumeSpike = EligibilityThresholds.Default.MinVolumeSpike,
            DefensiveMinVolumeSpike = EligibilityThresholds.Default.DefensiveMinVolumeSpike,
            MinResistanceDistance = EligibilityThresholds.Default.MinResistanceDistance,
            MinResistanceDistanceAtrMode = atrDistance,
            MinRiskReward = EligibilityThresholds.Default.MinRiskReward,
            MinRelativeStrengthPercent = EligibilityThresholds.Default.MinRelativeStrengthPercent,
            MinStopDistancePercent = EligibilityThresholds.Default.MinStopDistancePercent,
            MaxRiskReward = EligibilityThresholds.Default.MaxRiskReward,
            EnablePullbackBounce = EligibilityThresholds.Default.EnablePullbackBounce
        };
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
            !decimal.TryParse(txtMinResistDistanceAtr.Text, out decimal minResistDistanceAtr) ||
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
            MinResistanceDistanceAtrMode = minResistDistanceAtr,
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
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
        txtSummaryResult.Text = "";
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            int? evaluationHoursOverride = int.TryParse(txtEvaluationHoursOverride.Text, out int overrideHours) && overrideHours > 0
                ? overrideHours
                : null;

            var summary = await backtester.RunAsync(
                symbols,
                start,
                end,
                profile,
                thresholds,
                riskMode: GetSelectedRiskMode(),
                evaluationHoursOverride: evaluationHoursOverride,
                onProgress: (message, percent) => Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = message;
                    pbProgress.Value = percent;
                }),
                cancellationToken: _cts.Token);

            await SaveRunResultAsync("Rodar Backtest", symbols, start, end, profile, thresholds, GetSelectedRiskMode(), evaluationHoursOverride, summary);

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
            DrawEquityCurve(summary.Trades);
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
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
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
                    MinResistanceDistanceAtrMode = baseThresholds.MinResistanceDistanceAtrMode,
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
                    riskMode: GetSelectedRiskMode(),
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[RR≥{rr}] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync($"RR ≥ {rr}", symbols, start, end, profile, scenarioThresholds, GetSelectedRiskMode(), null, summary);

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
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
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
                    MinResistanceDistanceAtrMode = baseThresholds.MinResistanceDistanceAtrMode,
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
                    riskMode: GetSelectedRiskMode(),
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[Stop≥{stopMin}%] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync($"Stop ≥ {stopMin}%", symbols, start, end, profile, scenarioThresholds, GetSelectedRiskMode(), null, summary);

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
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
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
                    MinResistanceDistanceAtrMode = baseThresholds.MinResistanceDistanceAtrMode,
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
                    riskMode: GetSelectedRiskMode(),
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[RR≤{maxRR}] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync(
                    maxRR >= 999m ? "Sem teto" : $"RR ≤ {maxRR}",
                    symbols, start, end, profile, scenarioThresholds, GetSelectedRiskMode(), null, summary);

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
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
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
                    MinResistanceDistanceAtrMode = baseThresholds.MinResistanceDistanceAtrMode,
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
                    riskMode: GetSelectedRiskMode(),
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[{scenario.Label}] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync(scenario.Label, symbols, start, end, profile, scenarioThresholds, GetSelectedRiskMode(), null, summary);

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

    private async void BtnFreezeWithFullHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(txtTopN.Text, out int topN) || topN <= 0)
        {
            MessageBox.Show("Informe um número válido em \"Top automático\" antes de congelar (ex.: 100).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(txtHistoryYears.Text, out int historyYears) || historyYears <= 0)
        {
            MessageBox.Show("Informe um número válido de anos de histórico mínimo (ex.: 5).", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetRunningState(true);
        _cts = new CancellationTokenSource();

        try
        {
            txtStatus.Text = "Buscando lista completa de moedas por volume...";
            var allSymbolsByVolume = await _marketData.GetUsdtSymbolsAsync();

            var checkDate = DateTime.UtcNow.AddYears(-historyYears);
            var qualifying = new List<string>();
            int checkedCount = 0;
            const int maxCandidatesToCheck = 400; // limite de segurança pra não escanear a lista inteira indefinidamente

            foreach (var symbol in allSymbolsByVolume)
            {
                if (qualifying.Count >= topN || checkedCount >= maxCandidatesToCheck)
                    break;

                _cts.Token.ThrowIfCancellationRequested();
                checkedCount++;

                pbProgress.Value = qualifying.Count * 100.0 / topN;
                txtStatus.Text = $"Verificando histórico de {symbol}... ({qualifying.Count}/{topN} encontradas, {checkedCount} checadas)";

                try
                {
                    var probe = await _marketData.GetHistoricalCandlesAsync(
                        symbol, "1d", checkDate, checkDate.AddDays(3), _cts.Token);

                    if (probe.Count > 0)
                        qualifying.Add(symbol);
                }
                catch
                {
                    // Símbolo com erro na checagem — pula, não conta como qualificado.
                }

                await Task.Delay(150, _cts.Token);
            }

            txtManualSymbols.Text = string.Join(",", qualifying);
            rbManual.IsChecked = true;
            await _settingsRepository.SaveManualSymbolListAsync(txtManualSymbols.Text);

            if (qualifying.Count < topN)
            {
                MessageBox.Show(
                    $"Só encontrei {qualifying.Count} moedas com pelo menos {historyYears} ano(s) de histórico " +
                    $"(verifiquei as {checkedCount} mais líquidas). Considere reduzir o número de anos ou o alvo de moedas.",
                    "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            txtStatus.Text = $"Lista congelada: {qualifying.Count} moedas com pelo menos {historyYears} ano(s) de histórico garantido.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Busca cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível montar a lista.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
            pbProgress.Value = 0;
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
            await _settingsRepository.SaveManualSymbolListAsync(txtManualSymbols.Text);

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
        var defaultThresholds = BuildDefaultThresholdsWithCurrentAtrDistance();

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
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
                    riskMode: GetSelectedRiskMode(),
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[{label}] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync(label, symbols, periodStart, periodEnd, profile, defaultThresholds, GetSelectedRiskMode(), null, summary);

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

            var spanStart = DateTime.SpecifyKind(anchorEnd.AddYears(-periodCount * periodYears), DateTimeKind.Utc);
            await SaveRunResultAsync("TOTAL (todos os períodos juntos)", symbols, spanStart, anchorEnd, profile, defaultThresholds, GetSelectedRiskMode(), null, pooledSummary);

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
            var orderedAllTrades = allTrades.OrderBy(t => t.ExitTime).ToList();
            dgTrades.ItemsSource = orderedAllTrades;
            DrawEquityCurve(orderedAllTrades);

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

    private async void BtnCompareRiskMode_Click(object sender, RoutedEventArgs e)
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

        var defaultThresholds = BuildDefaultThresholdsWithCurrentAtrDistance();

        var modesToTest = new[]
        {
            (Mode: RiskCalculationMode.SwingBased, Label: "Swing"),
            (Mode: RiskCalculationMode.AtrBased, Label: "ATR")
        };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var modeInfo in modesToTest)
            {
                var allTrades = new List<BacktestTradeResult>();
                var aggregatedDiagnostics = new FilterDiagnostics();

                for (int i = periodCount; i >= 1; i--)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var periodEnd = DateTime.SpecifyKind(anchorEnd.AddYears(-(i - 1) * periodYears), DateTimeKind.Utc);
                    var periodStart = DateTime.SpecifyKind(anchorEnd.AddYears(-i * periodYears), DateTimeKind.Utc);

                    string periodLabel = $"[{modeInfo.Label}] {periodStart:dd/MM/yy} – {periodEnd:dd/MM/yy}";

                    var summary = await backtester.RunAsync(
                        symbols,
                        periodStart,
                        periodEnd,
                        profile,
                        defaultThresholds,
                        riskMode: modeInfo.Mode,
                        onProgress: (message, percent) => Dispatcher.Invoke(() =>
                        {
                            txtStatus.Text = $"{periodLabel} {message}";
                            pbProgress.Value = percent;
                        }),
                        cancellationToken: _cts.Token);

                    await SaveRunResultAsync($"[{modeInfo.Label}] {periodStart:dd/MM/yy}-{periodEnd:dd/MM/yy}",
                        symbols, periodStart, periodEnd, profile, defaultThresholds, modeInfo.Mode, null, summary);

                    allTrades.AddRange(summary.Trades);
                    StrategyBacktester.MergeDiagnostics(aggregatedDiagnostics, summary.Diagnostics);
                }

                // Só a linha TOTAL de cada modo entra na comparação — os períodos individuais
                // ficariam repetidos demais (2 modos x N períodos); o que importa aqui é o
                // agregado de cada modo, pra comparação direta.
                var pooledSummary = StrategyBacktester.BuildSummary(allTrades, aggregatedDiagnostics, new List<string>());

                var spanStart = DateTime.SpecifyKind(anchorEnd.AddYears(-periodCount * periodYears), DateTimeKind.Utc);
                await SaveRunResultAsync($"{modeInfo.Label} — TOTAL ({periodCount} período(s))",
                    symbols, spanStart, anchorEnd, profile, defaultThresholds, modeInfo.Mode, null, pooledSummary);

                results.Add(new ScenarioResult
                {
                    Label = $"{modeInfo.Label} — TOTAL ({periodCount} período(s))",
                    TotalTrades = pooledSummary.TotalTrades,
                    WinRate = pooledSummary.WinRate,
                    TotalReturnPercent = pooledSummary.TotalReturnPercent,
                    MaxDrawdownPercent = pooledSummary.MaxDrawdownPercent,
                    ProfitFactor = pooledSummary.ProfitFactor,
                    AvgRiskRewardAtEntry = pooledSummary.AvgRiskRewardAtEntry,
                    BreakEvenWinRate = pooledSummary.BreakEvenWinRate,
                    Edge = pooledSummary.Edge
                });

                if (modeInfo.Mode == GetSelectedRiskMode())
                {
                    var orderedModeTrades = allTrades.OrderBy(t => t.ExitTime).ToList();
                    dgTrades.ItemsSource = orderedModeTrades;
                    DrawEquityCurve(orderedModeTrades);
                }
            }

            dgComparison.ItemsSource = results;
            dgComparison.Visibility = Visibility.Visible;

            txtSummaryResult.Text =
                $"Comparação Swing vs ATR concluída — mesmos {periodCount} período(s) de {periodYears} ano(s), mesmas {symbols.Count} moedas, " +
                "mesma configuração padrão de elegibilidade nos dois casos. Só muda como o alvo (Take Profit) e o stop (Stop Loss) são calculados: " +
                "Swing usa a máxima/mínima dos últimos 50 candles; ATR usa múltiplos da volatilidade real de cada ativo " +
                $"(stop={ScannerSettings.AtrStopMultiplier}x ATR, alvo={ScannerSettings.AtrTargetMultiplier}x ATR). " +
                "A tabela de operações abaixo mostra o modo atualmente selecionado nos rádios \"Cálculo de Risco\" acima.";
            txtStatus.Text = "Concluído.";
        }
        catch (OperationCanceledException)
        {
            txtStatus.Text = "Comparação cancelada.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao comparar modos de risco.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
            txtStatus.Text = "";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private async void BtnCompareAtrDistanceScenarios_Click(object sender, RoutedEventArgs e)
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

        // Esse comparador só faz sentido no modo ATR — força o modo independente do rádio,
        // já que estamos testando especificamente esse limiar.
        decimal[] distanceScenarios = { 5m, 8m, 10m, 15m, 20m };

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var distance in distanceScenarios)
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
                    MinResistanceDistanceAtrMode = distance,
                    MinRiskReward = baseThresholds.MinRiskReward,
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
                    riskMode: RiskCalculationMode.AtrBased,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[Dist.ATR≥{distance}%] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync($"Dist. ATR ≥ {distance}%", symbols, start, end, profile, scenarioThresholds, RiskCalculationMode.AtrBased, null, summary);

                results.Add(new ScenarioResult
                {
                    Label = $"Dist. ATR ≥ {distance}%",
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
            txtSummaryResult.Text = "Comparação de distância mínima de resistência (modo ATR) concluída — modo ATR forçado " +
                                     "independente do rádio selecionado, já que esse teste só faz sentido nesse modo. " +
                                     "Os outros limiares ficaram fixos nos valores informados acima.";
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

    private void CnvEquityCurve_SizeChanged(object sender, SizeChangedEventArgs e) => DrawEquityCurve(_lastDisplayedTrades);

    private void DrawEquityCurve(List<BacktestTradeResult> trades)
    {
        _lastDisplayedTrades = trades;
        cnvEquityCurve.Children.Clear();

        if (trades.Count == 0)
            return;

        var ordered = trades.OrderBy(t => t.ExitTime).ToList();

        decimal running = 0;
        var points = new List<decimal> { 0 };
        foreach (var trade in ordered)
        {
            running += trade.OutcomePercent;
            points.Add(running);
        }

        double canvasWidth = cnvEquityCurve.ActualWidth;
        double canvasHeight = cnvEquityCurve.ActualHeight;

        if (canvasWidth <= 0 || canvasHeight <= 0)
            return; // ainda não teve um passe de layout — o SizeChanged vai chamar de novo

        decimal minValue = points.Min();
        decimal maxValue = points.Max();
        decimal range = maxValue - minValue;
        if (range == 0) range = 1;

        const double topMargin = 16;
        const double bottomMargin = 4;
        double plotHeight = canvasHeight - topMargin - bottomMargin;
        double xStep = points.Count > 1 ? canvasWidth / (points.Count - 1) : canvasWidth;

        double YFor(decimal value) => topMargin + (1 - (double)((value - minValue) / range)) * plotHeight;

        // Linha de referência em zero, se estiver dentro da faixa visível.
        if (minValue < 0 && maxValue > 0)
        {
            var zeroLine = new Line
            {
                X1 = 0,
                X2 = canvasWidth,
                Y1 = YFor(0m),
                Y2 = YFor(0m),
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            cnvEquityCurve.Children.Add(zeroLine);
        }

        var polyline = new Polyline
        {
            Stroke = points[^1] >= 0 ? Brushes.SteelBlue : Brushes.IndianRed,
            StrokeThickness = 2
        };

        for (int i = 0; i < points.Count; i++)
            polyline.Points.Add(new System.Windows.Point(i * xStep, YFor(points[i])));

        cnvEquityCurve.Children.Add(polyline);

        AddCurveLabel($"Pico: {maxValue:F1}%", 4, 2, Brushes.DarkGreen);
        AddCurveLabel($"Vale: {minValue:F1}%", 4, canvasHeight - 16, Brushes.DarkRed);

        var finalLabel = new TextBlock
        {
            Text = $"Final: {points[^1]:F1}% ({ordered.Count} trades)",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = points[^1] >= 0 ? Brushes.DarkGreen : Brushes.DarkRed
        };
        finalLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(finalLabel, canvasWidth - finalLabel.DesiredSize.Width - 4);
        Canvas.SetTop(finalLabel, 2);
        cnvEquityCurve.Children.Add(finalLabel);
    }

    private void AddCurveLabel(string text, double left, double top, System.Windows.Media.Brush color)
    {
        var label = new TextBlock { Text = text, FontSize = 10, Foreground = color };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        cnvEquityCurve.Children.Add(label);
    }

    private async void BtnCompareTimeoutScenarios_Click(object sender, RoutedEventArgs e)
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

        // Testa frações e múltiplos do timeout padrão do perfil selecionado —
        // funciona igual pra Intraday (24h) ou Swing (240h), já que é relativo.
        decimal[] multipliers = { 0.5m, 0.75m, 1.0m, 1.5m, 2.0m };
        var riskMode = GetSelectedRiskMode();

        SetRunningState(true);
        dgTrades.ItemsSource = null;
        cnvEquityCurve.Children.Clear();
        _lastDisplayedTrades = new List<BacktestTradeResult>();
        txtSummaryResult.Text = "";
        var results = new List<ScenarioResult>();
        _cts = new CancellationTokenSource();

        try
        {
            var backtester = new StrategyBacktester(_marketData, _assetAnalyzer);

            foreach (var multiplier in multipliers)
            {
                _cts.Token.ThrowIfCancellationRequested();

                int hours = (int)(profile.EvaluationHours * multiplier);

                var summary = await backtester.RunAsync(
                    symbols,
                    start,
                    end,
                    profile,
                    baseThresholds,
                    riskMode: riskMode,
                    evaluationHoursOverride: hours,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        txtStatus.Text = $"[Timeout={hours}h] {message}";
                        pbProgress.Value = percent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync($"Timeout = {hours}h ({multiplier:F2}x)", symbols, start, end, profile, baseThresholds, riskMode, hours, summary);

                results.Add(new ScenarioResult
                {
                    Label = $"Timeout = {hours}h ({multiplier:F2}x padrão)",
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
            txtSummaryResult.Text = $"Comparação de timeout concluída — perfil {profile.Name} (padrão: {profile.EvaluationHours}h), " +
                                     $"modo de risco {(riskMode == RiskCalculationMode.AtrBased ? "ATR" : "Swing")}. " +
                                     "Os outros limiares ficaram fixos nos valores informados acima.";
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

    private void SetRunningState(bool isRunning)
    {
        btnRun.IsEnabled = !isRunning;
        btnCompare.IsEnabled = !isRunning;
        btnCompareStop.IsEnabled = !isRunning;
        btnCompareMaxRR.IsEnabled = !isRunning;
        btnCompareNewPaths.IsEnabled = !isRunning;
        btnComparePeriods.IsEnabled = !isRunning;
        btnCompareRiskMode.IsEnabled = !isRunning;
        btnCompareAtrDistance.IsEnabled = !isRunning;
        btnCompareTimeout.IsEnabled = !isRunning;
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