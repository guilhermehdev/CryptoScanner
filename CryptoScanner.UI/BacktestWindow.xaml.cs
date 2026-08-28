using CryptoScanner.Application.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Core.Utilities;
using CryptoScanner.Infrastructure.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        dpStart.SelectedDate = DateTime.Today.AddYears(-6);

        txtMinScore.Text = ScannerSettings.BuyOpportunityScore.ToString("F0");
        txtMinResistDistance.Text = ScannerSettings.MinResistanceDistance.ToString("F0");
        txtMinResistDistanceAtr.Text = "10"; // provisório — a comparar empiricamente
        txtMinResistDistancePartialExits.Text = "4"; // provisório — a comparar empiricamente
        txtMinVolumeSpike.Text = ScannerSettings.MinVolumeSpike.ToString("F2");
        txtMinRiskReward.Text = 2.0m.ToString("F1"); // valor validado real do modo Swing+Resistência Pontuada (confirmado em ScannerService.cs) — corrigido de 1,5 (16/08/2026)
        txtMinStopDistance.Text = "0"; // 0 = sem piso, reproduz o comportamento atual do app ao vivo
        txtMaxStopDistance.Text = "25";
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
        EligibilityThresholds thresholds, RiskCalculationMode riskMode, int? evaluationHoursOverride,
        decimal? tp1Fraction = null, decimal? tp2Fraction = null, bool disableTimeout = false,
        TradeDirection direction = TradeDirection.Long, bool useInvertedRsiMomentum = false)
    {
        var sb = new StringBuilder();
        sb.Append(profile.Name).Append('|');
        sb.Append(riskMode).Append('|');
        sb.Append(direction).Append('|');
        sb.Append(start.ToString("O")).Append('|');
        sb.Append(end.ToString("O")).Append('|');
        sb.Append(string.Join(",", symbols.OrderBy(s => s, StringComparer.Ordinal))).Append('|');
        sb.Append(thresholds.BuyOpportunityScore).Append('|');
        sb.Append(thresholds.MinResistanceDistance).Append('|');
        sb.Append(thresholds.MinResistanceDistanceAtrMode).Append('|');
        sb.Append(thresholds.MinResistanceDistancePartialExits).Append('|');
        sb.Append(thresholds.MinVolumeSpike).Append('|');
        sb.Append(thresholds.MinRiskReward).Append('|');
        sb.Append(thresholds.MinStopDistancePercent).Append('|');
        sb.Append(thresholds.MaxStopDistancePercent).Append('|');
        sb.Append(thresholds.MaxRiskReward).Append('|');
        sb.Append(thresholds.EnablePullbackBounce).Append('|');
        sb.Append(thresholds.EnableBollingerScoring).Append('|');
        sb.Append(thresholds.EnableVolatilityScoringPhaseB).Append('|');
        sb.Append(thresholds.EnableMultiTimeframe).Append('|');
        sb.Append(thresholds.RequireBearishMomentumConfirmed).Append('|');
        sb.Append(thresholds.EnableLowRsiPath).Append('|');
        sb.Append(thresholds.BlockMeanReversionInBear).Append('|');
        sb.Append(thresholds.LimitAtrForMeanReversion).Append('|');
        sb.Append(useInvertedRsiMomentum).Append('|');
        sb.Append(evaluationHoursOverride?.ToString() ?? "default").Append('|');
        sb.Append(tp1Fraction?.ToString() ?? "default").Append('|');
        sb.Append(tp2Fraction?.ToString() ?? "default").Append('|');
        sb.Append(disableTimeout).Append('|');
        sb.Append(StrategyBacktester.EngineVersion);

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private async Task SaveRunResultAsync(
        string label, IReadOnlyList<string> symbols, DateTime start, DateTime end, ScanProfile profile,
        EligibilityThresholds thresholds, RiskCalculationMode riskMode, int? evaluationHoursOverride,
        BacktestSummary summary, decimal? tp1Fraction = null, decimal? tp2Fraction = null, bool disableTimeout = false,
        TradeDirection direction = TradeDirection.Long, bool useInvertedRsiMomentum = false)
    {
        try
        {
            // direction entra na assinatura — sem isso, um teste de Venda com o mesmo
            // perfil/modo/datas/limiares que um teste de Compra (ou de Venda anterior)
            // gerava a MESMA assinatura, e o sistema recusava salvar de novo (achando
            // que já existia um teste idêntico), mantendo o registro antigo — com a hora
            // antiga. Bug real, não comportamento esperado.
            string signature = ComputeSignature(symbols, start, end, profile, thresholds, riskMode, evaluationHoursOverride, tp1Fraction, tp2Fraction, disableTimeout, direction, useInvertedRsiMomentum);

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
                MaxStopDistancePercent = thresholds.MaxStopDistancePercent,
                MaxRiskReward = thresholds.MaxRiskReward,
                EnablePullbackBounce = thresholds.EnablePullbackBounce,
                EnableBollingerScoring = thresholds.EnableBollingerScoring,
                EnableVolatilityScoringPhaseB = thresholds.EnableVolatilityScoringPhaseB,
                Tp1Fraction = tp1Fraction,
                Tp2Fraction = tp2Fraction,
                DisableTimeout = disableTimeout,
                Diagnostics = summary.Diagnostics.Summary,
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
        if (rbRiskMeanReversion.IsChecked == true) return RiskCalculationMode.MeanReversionScalp;
        if (rbRiskBollingerReversal.IsChecked == true) return RiskCalculationMode.BollingerReversal;
        // Fallback pro modo validado real — "Swing (atual)" (SwingBased) saiu da tela
        // (22/08/2026), então não faz mais sentido cair nele se nenhum rádio bater
        // (o que não deveria acontecer de qualquer forma, já que rbRiskPartialExits
        // nasce marcado e o grupo é mutuamente exclusivo).
        return RiskCalculationMode.SwingWithPartialExits;
    }

    // Fase 1 do lado de venda — hoje só conectado no botão "Rodar Backtest" (BtnRun_Click).
    // Os comparadores continuam Long-only por enquanto; se a Venda mostrar sinal de vida,
    // isso se estende pros comparadores relevantes depois, não em bloco de uma vez.
    private TradeDirection GetSelectedDirection()
    {
        return rbDirectionShort.IsChecked == true ? TradeDirection.Short : TradeDirection.Long;
    }

    /// <summary>
    /// Config validada REAL por perfil (16/08/2026) — corrige o método antigo
    /// (BuildDefaultThresholdsWithCurrentAtrDistance), que usava os genéricos do
    /// ScannerSettings (RR=3, sem Caminho A) em vez dos valores calibrados por perfil que
    /// ScannerService.cs realmente usa ao vivo hoje (SwingValidatedThresholds/
    /// IntradayValidatedThresholds). Valores copiados diretamente de lá — se o app ao vivo
    /// mudar, esse método precisa ser atualizado junto. Só cobre Swing e Intraday; Scalp
    /// continua bloqueado (ver guard em BtnComparePeriods_Click) porque o próprio
    /// ScannerService.cs marca esse perfil como "NÃO VALIDADO — não expor no app ao vivo".
    /// </summary>
    private static EligibilityThresholds BuildLiveValidatedThresholds(ScanProfile profile)
    {
        bool isIntraday = profile.Name == ScanProfile.Intraday.Name;

        return new EligibilityThresholds
        {
            BuyOpportunityScore = ScannerSettings.BuyOpportunityScore,
            BearRegimePenalty = ScannerSettings.BearRegimePenalty,
            SidewaysRegimePenalty = ScannerSettings.SidewaysRegimePenalty,
            MinVolumeSpike = ScannerSettings.MinVolumeSpike,
            DefensiveMinVolumeSpike = ScannerSettings.DefensiveMinVolumeSpike,
            MinResistanceDistance = ScannerSettings.MinResistanceDistance,
            MinResistanceDistanceAtrMode = ScannerSettings.MinResistanceDistance, // não usado nesse modo
            MinResistanceDistancePartialExits = isIntraday ? 15m : 4m,
            MinRiskReward = isIntraday ? 2.5m : 2.0m,
            MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
            MinStopDistancePercent = 0m,
            MaxStopDistancePercent = 25m,
            MaxRiskReward = 999m,
            EnablePullbackBounce = !isIntraday, // Caminho A: só no Swing
            EnableBollingerScoring = true, // ligado nos 2 perfis
            EnableVolatilityScoringPhaseB = false,
            EnableMultiTimeframe = false
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
            !decimal.TryParse(txtMinResistDistancePartialExits.Text, out decimal minResistDistancePartialExits) ||
            !decimal.TryParse(txtMinVolumeSpike.Text, out decimal minVolumeSpike) ||
            !decimal.TryParse(txtMinRiskReward.Text, out decimal minRiskReward) ||
            !decimal.TryParse(txtMinStopDistance.Text, out decimal minStopDistance) ||
            !decimal.TryParse(txtMaxStopDistance.Text, out decimal maxStopDistance) ||
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
            MinResistanceDistancePartialExits = minResistDistancePartialExits,
            MinRiskReward = minRiskReward,
            MinRelativeStrengthPercent = ScannerSettings.MinRelativeStrengthPercent,
            MinStopDistancePercent = minStopDistance,
            MaxStopDistancePercent = maxStopDistance,
            MaxRiskReward = maxRiskReward,
            EnablePullbackBounce = chkEnablePullbackBounce.IsChecked == true, // Caminho A reexposto na tela — antes fixo em false
            // Bollinger Scoring — validado e ativo nos 2 perfis reais (Swing e Intraday) via
            // ScannerService.cs. Checkbox removido (22/08/2026); sempre true agora, já que
            // nunca faz sentido testar sem ele dado que a config real sempre usa.
            EnableBollingerScoring = true,
            EnableVolatilityScoringPhaseB = chkEnableVolatilityScoringPhaseB.IsChecked == true,
            EnableMultiTimeframe = chkEnableMultiTimeframe.IsChecked == true,
            EnableMeanReversionScalp = chkEnableMeanReversionScalp.IsChecked == true,
            BlockMeanReversionInBear = chkBlockMeanReversionInBear.IsChecked == true,
            LimitAtrForMeanReversion = chkLimitAtrForMeanReversion.IsChecked == true,
            EnableBollingerReversal = chkEnableBollingerReversal.IsChecked == true,
            // RequireBearishMomentumConfirmed (Venda) e EnableLowRsiPath (Compra) — testados
            // e descartados (ver memória do projeto: efeito negativo no bear market isolado
            // / trades novos sem catalisador de rompimento). Checkboxes removidos da tela
            // (16/08/2026); campos mantidos em EligibilityThresholds caso retomados no futuro.
            RequireBearishMomentumConfirmed = false,
            EnableLowRsiPath = false
        };

        return true;
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDateRange(out var start, out var end))
            return;

        if (!TryBuildThresholds(out var thresholds))
            return;

        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : rbBacktestScalp.IsChecked == true ? ScanProfile.Scalp : ScanProfile.Swing;
        var direction = GetSelectedDirection();

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

            bool disableTimeout = chkDisableTimeout.IsChecked == true;

            // Momentum RSI invertido — testado e descartado (efeito quase nulo na config
            // validada, 5% de peso é fraco demais pra mover a agulha). Checkbox removido
            // da tela (16/08/2026) pra reduzir a quantidade de controles experimentais
            // visíveis; parâmetro mantido no motor (RunAsync/Analyze) caso essa linha seja
            // retomada no futuro — bastaria reativar aqui.
            const bool useInvertedRsiMomentum = false;

            var summary = await backtester.RunAsync(
                symbols,
                start,
                end,
                profile,
                thresholds,
                riskMode: GetSelectedRiskMode(),
                evaluationHoursOverride: evaluationHoursOverride,
                disableTimeout: disableTimeout,
                direction: direction,
                useInvertedRsiMomentum: useInvertedRsiMomentum,
                onProgress: (message, percent) => Dispatcher.Invoke(() =>
                {
                    txtStatus.Text = message;
                    pbProgress.Value = percent;
                }),
                cancellationToken: _cts.Token);

            await SaveRunResultAsync(
                direction == TradeDirection.Short ? "Rodar Backtest (VENDA)" : "Rodar Backtest",
                symbols, start, end, profile, thresholds, GetSelectedRiskMode(), evaluationHoursOverride, summary, disableTimeout: disableTimeout, direction: direction, useInvertedRsiMomentum: useInvertedRsiMomentum);

            string skippedInfo = summary.SkippedSymbols.Count > 0
                ? $"\n\n⚠ {summary.SkippedSymbols.Count} de {symbols.Count} moedas não entraram no teste:\n" +
                  string.Join("\n", summary.SkippedSymbols)
                : "";

            string maxStopText = thresholds.MaxStopDistancePercent < 999m
                ? $"{thresholds.MaxStopDistancePercent:F0}%"
                : "sem teto";

            txtSummaryResult.Text =
                $"Direção: {(direction == TradeDirection.Short ? "VENDA (Fase 1)" : "Compra")} | " +
                $"Score≥{thresholds.BuyOpportunityScore:F0} | RR mín.={thresholds.MinRiskReward:F1} | Stop mín.={thresholds.MinStopDistancePercent:F0}% | Stop máx.={maxStopText}" +
                (disableTimeout ? " | Timeout=DESATIVADO (só TP/SL)" : "") + "\n\n" +
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

    private void BtnCopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtSummaryResult.Text))
        {
            MessageBox.Show("Nenhum resultado pra copiar ainda — rode um teste primeiro.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(txtSummaryResult.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível copiar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Exporta o grid de trades atualmente exibido (_lastDisplayedTrades, o mesmo que
    /// alimenta dgTrades e a curva de equity) pra um CSV — inclui as colunas de
    /// instrumentação (HadBearishMomentumConfirmed/HadBearishRsiDivergence/
    /// HadSwingHighDataAvailable), que não aparecem no "Copiar Resumo" agregado.
    /// Delimitador ';' (não ',') pra abrir bem direto no Excel em configuração PT-BR,
    /// que costuma usar vírgula como separador decimal.
    /// </summary>
    /// <summary>
    /// Fase 3 do roadmap, Passo 2 — abre a análise por fator (bucket por RSI/ADX/Score/etc.)
    /// em cima dos trades atualmente exibidos (_lastDisplayedTrades, mesmo dado que alimenta
    /// dgTrades/CSV). Substitui o processo manual de exportar CSV e analisar via Python.
    /// </summary>
    private void BtnAnalyzeFactors_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDisplayedTrades.Count == 0)
        {
            MessageBox.Show("Nenhuma operação pra analisar ainda — rode um teste primeiro.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new BacktestAnalyticsWindow(_lastDisplayedTrades)
        {
            Owner = this
        };
        window.Show();
    }

    private void BtnExportTradesCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDisplayedTrades.Count == 0)
        {
            MessageBox.Show("Nenhuma operação pra exportar ainda — rode um teste primeiro.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"backtest_trades_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Filter = "Arquivo CSV (*.csv)|*.csv",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var culture = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";",
                "Symbol", "Direction", "EntryTime", "EntryPrice", "ExitTime", "ExitPrice",
                "OutcomePercent", "ExitReason", "Signal", "Score",
                "ResistanceDistancePercent", "SupportDistancePercent", "RiskRewardAtEntry",
                "HadBearishMomentumConfirmed", "HadBearishRsiDivergence", "HadSwingHighDataAvailable",
                "Rsi", "Adx", "AtrPercent", "EmaDistanceAtr", "SwingUsageAtr",
                "VolumeSpike", "VolumeImbalance", "RelativeStrength",
                "TrendScore", "StructureScore", "VolumeScore", "CandleScore", "SetupScore",
                "MomentumScore", "VolatilityScore", "TrendStrengthScore",
                "PatternName", "SmartMoneyLabel", "BreakoutSource", "MarketRegime",
                "IsBullTrap", "IsBearTrap"));

            foreach (var t in _lastDisplayedTrades)
            {
                sb.AppendLine(string.Join(";",
                    t.Symbol,
                    t.Direction,
                    t.EntryTime.ToString("yyyy-MM-dd HH:mm", culture),
                    t.EntryPrice.ToString("0.########", culture),
                    t.ExitTime.ToString("yyyy-MM-dd HH:mm", culture),
                    t.ExitPrice.ToString("0.########", culture),
                    t.OutcomePercent.ToString("F2", culture),
                    t.ExitReason,
                    t.Signal,
                    t.Score.ToString("F2", culture),
                    t.ResistanceDistancePercent.ToString("F2", culture),
                    t.SupportDistancePercent.ToString("F2", culture),
                    t.RiskRewardAtEntry.ToString("F2", culture),
                    t.HadBearishMomentumConfirmed,
                    t.HadBearishRsiDivergence,
                    t.HadSwingHighDataAvailable,
                    t.Rsi.ToString("F2", culture),
                    t.Adx.ToString("F2", culture),
                    t.AtrPercent.ToString("F2", culture),
                    t.EmaDistanceAtr.ToString("F2", culture),
                    t.SwingUsageAtr.ToString("F2", culture),
                    t.VolumeSpike.ToString("F2", culture),
                    t.VolumeImbalance.ToString("F2", culture),
                    t.RelativeStrength.ToString("F2", culture),
                    t.TrendScore,
                    t.StructureScore,
                    t.VolumeScore,
                    t.CandleScore,
                    t.SetupScore,
                    t.MomentumScore,
                    t.VolatilityScore,
                    t.TrendStrengthScore,
                    t.PatternName,
                    t.SmartMoneyLabel,
                    t.BreakoutSource,
                    t.MarketRegime,
                    t.IsBullTrap,
                    t.IsBearTrap));
            }

            // BOM UTF-8 — sem isso, o Excel às vezes abre acentos/caracteres especiais
            // corrompidos mesmo o arquivo estando salvo certo.
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));

            MessageBox.Show($"Exportado com sucesso:\n{dialog.FileName}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível exportar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
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
        var profile = rbBacktestIntraday.IsChecked == true ? ScanProfile.Intraday : rbBacktestScalp.IsChecked == true ? ScanProfile.Scalp : ScanProfile.Swing;

        // Scalp ainda não foi validado (ver ScannerService.cs: "NÃO VALIDADO — não expor no
        // app ao vivo") — esse botão promete reproduzir a config real, então não faz sentido
        // rodar num perfil que não tem config real pra reproduzir.
        if (profile.Name == ScanProfile.Scalp.Name)
        {
            MessageBox.Show(
                "O perfil Scalp ainda não foi validado (ver ScannerService.cs) — \"Rodar Padrão Scanner\" só cobre Swing e Intraday, os únicos perfis promovidos pro app ao vivo.",
                "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

        // Config validada REAL do perfil (Swing ou Intraday) — a mesma que roda ao vivo hoje.
        var liveThresholds = BuildLiveValidatedThresholds(profile);

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

            int periodStepIndex = 0;
            for (int i = periodCount; i >= 1; i--)
            {
                _cts.Token.ThrowIfCancellationRequested();
                periodStepIndex++;

                var periodEnd = DateTime.SpecifyKind(anchorEnd.AddYears(-(i - 1) * periodYears), DateTimeKind.Utc);
                var periodStart = DateTime.SpecifyKind(anchorEnd.AddYears(-i * periodYears), DateTimeKind.Utc);
                string label = $"{periodStart:dd/MM/yy} – {periodEnd:dd/MM/yy}";

                var summary = await backtester.RunAsync(
                    symbols,
                    periodStart,
                    periodEnd,
                    profile,
                    liveThresholds,
                    riskMode: RiskCalculationMode.SwingWithPartialExits,
                    onProgress: (message, percent) => Dispatcher.Invoke(() =>
                    {
                        double overallPercent = ((periodStepIndex - 1) * 100.0 + percent) / periodCount;
                        txtStatus.Text = $"[{periodStepIndex}/{periodCount}] [{label}] {message}";
                        pbProgress.Value = overallPercent;
                    }),
                    cancellationToken: _cts.Token);

                await SaveRunResultAsync(label, symbols, periodStart, periodEnd, profile, liveThresholds, RiskCalculationMode.SwingWithPartialExits, null, summary);

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
            await SaveRunResultAsync("TOTAL (todos os períodos juntos)", symbols, spanStart, anchorEnd, profile, liveThresholds, RiskCalculationMode.SwingWithPartialExits, null, pooledSummary);

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
                $"Padrão Scanner: configuração VALIDADA REAL do perfil {profile.Name} (a mesma que roda ao vivo hoje — " +
                $"ScannerService.cs) — RR≥{liveThresholds.MinRiskReward:F1}, Dist. Resist. Pontuada≥{liveThresholds.MinResistanceDistancePartialExits:F0}%, " +
                $"Caminho A={(liveThresholds.EnablePullbackBounce ? "ligado" : "desligado")}, Stop máx.={liveThresholds.MaxStopDistancePercent:F0}%, " +
                $"modo Swing+Resistência Pontuada forçado (independente do rádio selecionado) — testada em {periodCount} período(s) " +
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

    private void SetRunningState(bool isRunning)
    {
        btnRun.IsEnabled = !isRunning;
        btnComparePeriods.IsEnabled = !isRunning;
        btnExportTrades.IsEnabled = !isRunning;
        btnAnalyzeFactors.IsEnabled = !isRunning;
        btnCancel.IsEnabled = isRunning;
        pbProgress.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        if (!isRunning)
            _cts = null;
    }
}