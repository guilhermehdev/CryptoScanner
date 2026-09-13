using CryptoScanner.Application.Services;
using CryptoScanner.Application.Models;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Infrastructure.Sqlite;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Brushes = System.Windows.Media.Brushes;

namespace CryptoScanner.UI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly ScannerService _scanner;
    private readonly BuyingPressureHistoryService _pressureHistory;
    private readonly SqliteStrategyLabRepository _labRepository;
    private readonly StrategyLabService _lab;
    private readonly DispatcherTimer _labTimer=new() { Interval=TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _labClosed=new();
    private string _labStatus="Laboratório ativo";
    private int _pressureHistoryErrors;
    private string _lastPressureHistoryError = "";
    private readonly IWatchlistRepository _watchlistRepository;
    private readonly ISimulatedTradeRepository _simulatedTradeRepository;
    private readonly IBacktestRunResultRepository _runResultRepository;
    private readonly BinanceExchangeService _priceCheckService = new();
    private readonly IAlertSettingsRepository _alertSettingsRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly BinanceWebSocketService _webSocketService = new();
    private readonly CoinGeckoService _coinGeckoService = new();
    private readonly OllamaVisionAnalyzer _llmAnalyzer = new(new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
    private readonly ILlmOpinionRepository _llmOpinionRepository;
    private const int AutomaticLlmTopCount = 30;
    private readonly SemaphoreSlim _automaticLlmGate = new(1, 1);
    private readonly Dictionary<string, string> _automaticLlmLastSignature = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _automaticLlmTopSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _automaticLlmProfilesRunning = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SimulatedTrade> _lastSimulatedTrades = Array.Empty<SimulatedTrade>();
    private bool _showClosedTrades; // Diário mostra só "em andamento" por padrão
    private bool _isWindowLoaded;
    private IReadOnlyList<SignalHistory> _lastHistory = Array.Empty<SignalHistory>();
    private Forms.NotifyIcon? _trayIcon;
    private readonly HashSet<Window> _detachedChildWindows = new();

    // --- Scan Duplo (Swing + Intraday simultâneo) ---------------------------
    // Os 2 perfis escaneiam sempre, em paralelo. _viewedProfile controla só o
    // que aparece na tela agora — não é mais o gatilho de scan.
    private readonly Dictionary<string, IReadOnlyList<AssetScore>> _rankingsByProfile = new()
    {
        [ScanProfile.Swing.Name] = Array.Empty<AssetScore>(),
        [ScanProfile.Intraday.Name] = Array.Empty<AssetScore>()
    };
    private readonly Dictionary<string, DateTime> _scanCompletedAt = new();
    private readonly Dictionary<string, FilterDiagnostics?> _diagnosticsByProfile = new();
    private readonly Dictionary<string, bool> _isScanningByProfile = new();
    private ScanProfile _viewedProfile = ScanProfile.Swing;
    private string _lastMarketRegime = "—";
    // -------------------------------------------------------------------------

    public MainWindow()
    {
        InitializeComponent();
        var databasePath = GetDatabasePath();
        _watchlistRepository = new SqliteWatchlistRepository(databasePath);
        _simulatedTradeRepository = new SqliteSimulatedTradeRepository(databasePath);
        _runResultRepository = new SqliteBacktestRunResultRepository(databasePath);
        _alertSettingsRepository = new SqliteAlertSettingsRepository(databasePath);
        _appSettingsRepository = new SqliteAppSettingsRepository(databasePath);
        _llmOpinionRepository = new SqliteLlmOpinionRepository(databasePath);
        _pressureHistory = new BuyingPressureHistoryService(new SqliteBuyingPressureRepository(databasePath), _priceCheckService);
        _labRepository=new SqliteStrategyLabRepository(databasePath);
        _lab=new StrategyLabService(_labRepository,_priceCheckService);
        _labTimer.Tick+=async (_,_)=>await EvaluateLabAsync();
        _scanner = new ScannerService(
            new BinanceExchangeService(),
            new SqliteSignalRepository(databasePath),
            _watchlistRepository,
            new AssetAnalyzer(), _pressureHistory);
        _scanner.PressureHistoryError += ReportPressureHistoryError;

        Loaded += MainWindow_Loaded;
        _timer.Interval = TimeSpan.FromMinutes(1); // padrão inicial — o valor real (persistido) é carregado no Loaded
        _timer.Tick += Timer_Tick;

        InitializeTrayIcon();
        StateChanged += MainWindow_StateChanged;
        _webSocketService.PriceUpdated += OnWebSocketPriceUpdated;
        _webSocketService.CandleClosed += OnCandleClosed;
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            Text = "CryptoScanner"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Sair", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
            return;

        // WPF normally minimizes owned windows together with their owner. The
        // state transition can minimize them after this event, so detach and
        // restore them on the dispatcher after WPF finishes processing it.
        var children = OwnedWindows.Cast<Window>().ToArray();
        Hide();
        ShowInTaskbar = false;
        if (_trayIcon != null)
            _trayIcon.Visible = true;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            foreach (var child in children)
            {
                try
                {
                    child.Owner = null;
                    child.ShowInTaskbar = true;
                    if (!child.IsVisible)
                        child.Show();
                    if (child.WindowState == WindowState.Minimized)
                        child.WindowState = WindowState.Normal;
                    _detachedChildWindows.Add(child);
                }
                catch
                {
                    // A child that is closing or modal can disappear between the
                    // snapshot and the deferred detach; it needs no handling.
                }
            }
        }));
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        if (_trayIcon != null)
            _trayIcon.Visible = false;
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        _labTimer.Stop();_labClosed.Cancel();
        foreach (var child in _detachedChildWindows.ToArray())
        {
            try { child.Close(); } catch { }
        }
        _detachedChildWindows.Clear();
        _trayIcon?.Dispose();
        _ = _webSocketService.DisposeAsync().AsTask(); // fire-and-forget — app já está fechando
        base.OnClosed(e);
    }

    // Dispara os 2 perfis a cada tick — cada um com seu próprio lock, não se bloqueiam.
    private void Timer_Tick(object? sender, EventArgs e)
    {
        _ = RunScannerAsync(ScanProfile.Swing);
        _ = RunScannerAsync(ScanProfile.Intraday);
    }

    private async Task RunScannerAsync(ScanProfile profile)
    {
        if (_isScanningByProfile.GetValueOrDefault(profile.Name))
            return; // só bloqueia scan sobreposto do MESMO perfil — o outro perfil roda livre

        _isScanningByProfile[profile.Name] = true;
        UpdateAtualizarButtonState();
        popupBreakdown.IsOpen = false;

        try
        {
            var directions = GetSelectedScannerDirections();
            var shortExperimental = GetUseShortExperimentalProfile();
            var results = await Task.WhenAll(directions.Select(direction =>
                _scanner.RunAsync(profile, direction, shortExperimental: direction == TradeDirection.Short && shortExperimental)));
            var result = results.Length == 1 ? results[0] : MergeScannerResults(results);

            _lastHistory = result.History; // já é global (Signals não filtra por Profile)
            _rankingsByProfile[profile.Name] = result.Ranking;
            _diagnosticsByProfile[profile.Name] = result.Diagnostics;
            _ = AnalyzeTopRankingWithLlmAsync(profile, result.Ranking);
            _scanCompletedAt[profile.Name] = DateTime.Now;
            _lastMarketRegime = result.MarketRegime;

            // Só redesenha o grid de ranking se o perfil que terminou é o exibido agora —
            // senão o usuário veria o grid trocar sozinho enquanto olha o outro perfil.
            if (profile.Name == _viewedProfile.Name)
            {
                ApplyRankingFilter();
                _=RecordLabGridAsync(profile);
            }

            await DispatchAlertsAsync(result.NewSignals);
            await EvaluateSimulatedTradesAsync();
            await LoadSimulatedTradesAsync(); // mantém o resumo do Diário (e o espelho no Dashboard) sempre fresco
            try { await _pressureHistory.CompleteDueAsync(DateTimeOffset.UtcNow); }
            catch (Exception ex) { ReportPressureHistoryError($"Preços posteriores: {ex.Message}"); }

            UpdateDashboard(result.MarketRegime);
            _ = UpdateBtcDominanceAsync(); // não bloqueia o scan — atualiza o Dashboard quando terminar

            dgHistory.ItemsSource = result.History;
            txtWinRate.Text = result.History.Any(signal => signal.Evaluated) ? $"Acertos: {result.WinRate:F1}%" : "Sem resultados avaliados";
            txtAvgReturn.Text = result.History.Any(signal => signal.Evaluated) ? $"Retorno médio: {result.AverageReturn:F2}%" : "";
            txtPending.Text = $"Pendentes: {result.History.Count(signal => !signal.Evaluated)}";
            txtEvaluated.Text = $"Avaliados: {result.History.Count(signal => signal.Evaluated)}";

            RefreshDiagnosticsDisplay();
            RefreshTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível concluir a atualização do scanner ({profile.Name}).\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isScanningByProfile[profile.Name] = false;
            UpdateAtualizarButtonState();
        }
    }

    // Botão "Atualizar" fica desabilitado enquanto QUALQUER perfil estiver escaneando.
    private void UpdateAtualizarButtonState()
    {
        btAtualizar.IsEnabled = !_isScanningByProfile.Values.Any(scanning => scanning);
    }

    private static ScannerRunResult MergeScannerResults(IReadOnlyList<ScannerRunResult> results)
    {
        var first = results[0];
        var last = results[^1];
        var diagnostics = first.Diagnostics;

        foreach (var other in results.Skip(1))
        {
            foreach (var property in typeof(FilterDiagnostics).GetProperties()
                         .Where(p => p.PropertyType == typeof(int) && p.CanRead && p.CanWrite))
            {
                int total = (int)property.GetValue(diagnostics)! + (int)property.GetValue(other.Diagnostics)!;
                property.SetValue(diagnostics, total);
            }

            foreach (var pair in other.Diagnostics.CandidateTypes)
                diagnostics.CandidateTypes[pair.Key] = diagnostics.CandidateTypes.GetValueOrDefault(pair.Key) + pair.Value;
            foreach (var pair in other.Diagnostics.Errors)
                diagnostics.Errors[$"Short: {pair.Key}"] = pair.Value;
            foreach (var pair in other.Diagnostics.OnlyBlockedBy)
            {
                if (!diagnostics.OnlyBlockedBy.TryGetValue(pair.Key, out var symbols))
                    diagnostics.OnlyBlockedBy[pair.Key] = symbols = new();
                symbols.AddRange(pair.Value);
            }
            foreach (var pair in other.Diagnostics.Strategies)
            {
                if (diagnostics.Strategies.TryGetValue(pair.Key, out var existing))
                    existing.Merge(pair.Value);
                else
                    diagnostics.Strategies[pair.Key] = pair.Value;
            }
        }

        diagnostics.Analyses = results.SelectMany(r => r.Diagnostics.Analyses).ToList();
        diagnostics.Thresholds = null; // Long and Short can have different thresholds.
        diagnostics.Profile = $"{first.Diagnostics.Profile} · Ambos";
        diagnostics.StartedUtc = results.Min(r => r.Diagnostics.StartedUtc);
        diagnostics.CompletedUtc = results.Max(r => r.Diagnostics.CompletedUtc);

        return new ScannerRunResult
        {
            MarketRegime = first.MarketRegime,
            Ranking = results.SelectMany(r => r.Ranking)
                .GroupBy(a => $"{a.Symbol}|{a.Direction}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(a => a.Score).First())
                .OrderByDescending(a => a.Score)
                .ThenBy(a => a.Symbol)
                .ToList(),
            History = last.History,
            WinRate = last.WinRate,
            AverageReturn = last.AverageReturn,
            Diagnostics = diagnostics,
            NewSignals = results.SelectMany(r => r.NewSignals)
                .GroupBy(s => $"{s.Symbol}|{s.Signal}|{s.Profile}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList()
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isWindowLoaded = true;
        _labTimer.Start();
        _=EvaluateLabAsync();
        await LoadAutoScanIntervalAsync();
        try { await _llmOpinionRepository.InitializeAsync(); }
        catch (Exception ex) { txtLlmOpinion.Text = $"Histórico da LLM indisponível: {ex.Message}"; }

        // O WebSocket reage ao fechamento dos candles, mas o timer é a rede de
        // segurança para manter o scanner ativo quando a conexão não estiver disponível.
        // O intervalo já foi carregado acima, então a primeira varredura periódica usa a
        // configuração persistida pelo usuário.
        _timer.Start();

        try
        {
            await _webSocketService.ConnectAsync();
        }
        catch (Exception ex)
        {
            // Não conectar ao stream de preço em tempo real não deve impedir o app de
            // funcionar — só fica sem atualização instantânea; o scan normal continua
            // cobrindo tudo via REST, como sempre foi.
            MessageBox.Show(
                $"Não foi possível conectar ao stream de preços em tempo real. O app vai continuar funcionando normalmente, só sem atualização instantânea de preço.\n{ex.Message}",
                "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await LoadSimulatedTradesAsync();

        // Os 2 perfis rodam já na abertura do app, em paralelo.
        _ = RunScannerAsync(ScanProfile.Swing);
        _ = RunScannerAsync(ScanProfile.Intraday);
    }

    private const string AutoScanIntervalSettingKey = "AutoScanIntervalMinutes";

    private async Task LoadAutoScanIntervalAsync()
    {
        int minutes = 1; // padrão inicial, conforme combinado
        try
        {
            await _appSettingsRepository.InitializeAsync();
            string? saved = await _appSettingsRepository.GetAsync(AutoScanIntervalSettingKey);
            if (saved != null && int.TryParse(saved, out int savedMinutes) && savedMinutes >= 1)
                minutes = savedMinutes;
        }
        catch
        {
            // Falha ao ler configuração não deve impedir o app de abrir — usa o padrão.
        }

        txtAutoScanInterval.Text = minutes.ToString();
        _timer.Interval = TimeSpan.FromMinutes(minutes);
    }

    private async void TxtAutoScanInterval_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isWindowLoaded)
            return;

        if (!int.TryParse(txtAutoScanInterval.Text, out int minutes) || minutes < 1)
        {
            MessageBox.Show("Informe um número inteiro de minutos, mínimo 1.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            await LoadAutoScanIntervalAsync(); // reverte o campo pro último valor válido salvo
            return;
        }

        _timer.Interval = TimeSpan.FromMinutes(minutes);

        try
        {
            await _appSettingsRepository.InitializeAsync();
            await _appSettingsRepository.SetAsync(AutoScanIntervalSettingKey, minutes.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar o intervalo.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Agora só troca QUAL RANKING JÁ CALCULADO aparece na tela — não dispara scan novo.
    private void ProfileChanged(object sender, RoutedEventArgs e)
    {
        // Ignora o Checked disparado durante o carregamento inicial do XAML
        // (rbSwing já nasce com IsChecked="True").
        if (!_isWindowLoaded)
            return;

        _viewedProfile = ReferenceEquals(sender, rbIntraday) ? ScanProfile.Intraday : ScanProfile.Swing;
        ApplyRankingFilter();
        RefreshTitle();
    }

    private TradeDirection GetSelectedScannerDirection() =>
        rbScanShort?.IsChecked == true ? TradeDirection.Short : TradeDirection.Long;

    private bool IsBothDirectionsSelected() => rbScanBoth?.IsChecked == true;

    private IReadOnlyList<TradeDirection> GetSelectedScannerDirections() =>
        IsBothDirectionsSelected()
            ? new[] { TradeDirection.Long, TradeDirection.Short }
            : new[] { GetSelectedScannerDirection() };

    private bool GetUseShortExperimentalProfile() =>
        (GetSelectedScannerDirection() == TradeDirection.Short || IsBothDirectionsSelected()) && chkShortExperimental?.IsChecked == true;

    private void ScanDirectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_isWindowLoaded)
            return;

        _ = RunScannerAsync(ScanProfile.Swing);
        _ = RunScannerAsync(ScanProfile.Intraday);
    }

    private void ShortExperimentalChanged(object sender, RoutedEventArgs e)
    {
        if (!_isWindowLoaded)
            return;

        // A change to the experimental profile must refresh both rankings so the
        // visible grid and the background profile use exactly the same thresholds.
        _ = RunScannerAsync(ScanProfile.Swing);
        _ = RunScannerAsync(ScanProfile.Intraday);
    }

    private void BtnBacktestHistory_Click(object sender, RoutedEventArgs e)
    {
        var window = new BacktestHistoryWindow(_runResultRepository)
        {
            Owner = this
        };
        window.Show();
    }

    private void BtnAnalytics_Click(object sender, RoutedEventArgs e)
    {
        var window = new AnalyticsWindow(_lastHistory)
        {
            Owner = this
        };
        window.Show();
    }

    private void BtnFullBacktest_Click(object sender, RoutedEventArgs e)
    {
        var databasePath = GetDatabasePath();
        var cacheRepository = new SqliteCandleCacheRepository(databasePath);
        var cachingMarketData = new CachingMarketDataService(new BinanceExchangeService(), cacheRepository);
        var window = new BacktestWindow(cachingMarketData, new AssetAnalyzer(), databasePath)
        {
            Owner = this
        };
        window.Show();
    }

    private void BtnAlertSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new AlertSettingsWindow(_alertSettingsRepository)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void BtnPressureAnalysis_Click(object sender, RoutedEventArgs e)
    {
        new PressureAnalysisWindow(GetDatabasePath()) { Owner = this }.Show();
    }

    private void BtnStrategyLab_Click(object sender,RoutedEventArgs e) => new StrategyLabWindow(_labRepository) { Owner=this }.Show();

    private void BtnLlmHistory_Click(object sender, RoutedEventArgs e)
        => new LlmOpinionHistoryWindow(_llmOpinionRepository) { Owner = this }.Show();

    private async void BtnAnalyzeWithLlm_Click(object sender, RoutedEventArgs e)
    {
        if (dgRanking.SelectedItem is not AssetScore asset)
        {
            MessageBox.Show("Selecione uma moeda no ranking antes de chamar a LLM.", "Análise com LLM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        txtLlmOpinion.Text = $"LLM consultiva: analisando {asset.Symbol}…";
        try
        {
            var opinion = await _llmAnalyzer.AnalyzeAsync(null, BuildLlmIndicators(asset));
            txtLlmOpinion.Text = FormatLlmOpinion(asset, opinion);

            try
            {
                await _llmOpinionRepository.AddAsync(new LlmOpinionRecord
                {
                    CreatedAt = DateTime.Now,
                    Symbol = asset.Symbol,
                    Profile = _viewedProfile.Name,
                    ImagePath = "",
                    AnalysisPrice = asset.Close,
                    ScannerSignal = asset.DisplaySignal,
                    Decision = opinion.Decisao,
                    Direction = opinion.Direcao,
                    Confidence = opinion.Confianca,
                    Trend = opinion.Tendencia,
                    Entry = opinion.Entrada,
                    Stop = opinion.Stop,
                    Tp1 = opinion.Tp1,
                    Tp2 = opinion.Tp2,
                    Reasons = opinion.Motivos.Length == 0 ? "" : string.Join("; ", opinion.Motivos),
                    Risks = opinion.Riscos.Length == 0 ? "" : string.Join("; ", opinion.Riscos)
                });
                txtLlmOpinion.ToolTip = "Análise exibida e salva no histórico local.";
            }
            catch (Exception saveError)
            {
                txtLlmOpinion.ToolTip = $"Análise exibida, mas não foi salva: {saveError.Message}";
            }
        }
        catch (Exception ex)
        {
            txtLlmOpinion.Text = $"LLM consultiva indisponível: {ex.Message}";
        }
    }

    private object BuildLlmIndicators(AssetScore asset) => new
    {
        ativo = asset.Symbol,
        direcao = asset.Direction == TradeDirection.Short ? "VENDA" : "COMPRA",
        sinalScanner = asset.DisplaySignal,
        score = asset.Score,
        precoFechamento = asset.Close,
        precoAtual = asset.LivePrice,
        tendencia = asset.TrendDirection,
        regime = asset.MarketRegime,
        rsi = asset.Rsi,
        adx = asset.Adx,
        atrPercentual = asset.AtrPercent,
        volumeSpike = asset.VolumeSpike,
        pressaoCompradora = asset.BuyingPressureScore,
        fluxoVarejo = asset.RetailFlowScore,
        forcaRelativa = asset.RelativeStrength,
        suporte = asset.Support,
        resistencia = asset.Resistance,
        distanciaAlvoPercentual = asset.ResistanceDistance,
        distanciaStopPercentual = asset.SupportDistance,
        riscoRetorno = asset.RiskReward,
        padrao = asset.PatternName,
        armadilhaAlta = asset.IsBullTrap,
        armadilhaBaixa = asset.IsBearTrap
    };

    private static string FormatLlmOpinion(AssetScore asset, LlmTradeOpinion opinion)
    {
        var reasons = opinion.Motivos.Length == 0 ? "(sem motivos informados)" : string.Join("; ", opinion.Motivos);
        var risks = opinion.Riscos.Length == 0 ? "(nenhum risco informado)" : string.Join("; ", opinion.Riscos);
        var levels = opinion.Entrada is null && opinion.Stop is null && opinion.Tp1 is null && opinion.Tp2 is null
            ? "Níveis: não informados"
            : $"Entrada {opinion.Entrada?.ToString("0.########") ?? "—"} | Stop {opinion.Stop?.ToString("0.########") ?? "—"} | TP1 {opinion.Tp1?.ToString("0.########") ?? "—"} | TP2 {opinion.Tp2?.ToString("0.########") ?? "—"}";

        return $"LLM {asset.Symbol}: {opinion.Decisao} · {opinion.Direcao} · confiança {opinion.Confianca}/100 · {opinion.Tendencia}\n{levels}\nMotivos: {reasons}\nRiscos: {risks}\nSinal original do scanner: {asset.DisplaySignal}. A LLM é consultiva e não altera o ranking.";
    }

    private async Task LinkLatestLlmOpinionAsync(int simulatedTradeId, AssetScore asset, string profile)
    {
        try
        {
            var opinions = await _llmOpinionRepository.GetRecentAsync(200, _labClosed.Token);
            var now = DateTime.Now;
            var candidate = opinions
                .Where(opinion =>
                    opinion.SimulatedTradeId is null &&
                    opinion.Symbol.Equals(asset.Symbol, StringComparison.OrdinalIgnoreCase) &&
                    opinion.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase) &&
                    opinion.CreatedAt >= now.AddHours(-2) &&
                    opinion.CreatedAt <= now.AddMinutes(10))
                .OrderByDescending(opinion => opinion.CreatedAt)
                .FirstOrDefault();

            if (candidate != null)
                await _llmOpinionRepository.AttachTradeAsync(candidate.Id, simulatedTradeId, _labClosed.Token);
        }
        catch (OperationCanceledException) when (_labClosed.IsCancellationRequested)
        {
        }
        catch
        {
            // O vínculo é auxiliar e não pode impedir a abertura do trade.
        }
    }

    private async Task RecordLlmOutcomeAsync(int simulatedTradeId, decimal outcomePercent, string outcomeReason, DateTime outcomeAt)
    {
        try
        {
            await _llmOpinionRepository.UpdateOutcomeAsync(
                simulatedTradeId, outcomePercent, outcomeReason, outcomeAt, _labClosed.Token);
        }
        catch (OperationCanceledException) when (_labClosed.IsCancellationRequested)
        {
        }
        catch
        {
            // A saída do trade já foi persistida; falha no histórico será tentada em outra abertura.
        }
    }
    private static string BuildLlmAnalysisSignature(AssetScore asset)
        => string.Join("|",
            asset.Direction,
            asset.Close.ToString("G29", CultureInfo.InvariantCulture),
            asset.OpportunityScore.ToString("G29", CultureInfo.InvariantCulture),
            asset.DisplaySignal,
            asset.Resistance.ToString("G29", CultureInfo.InvariantCulture),
            asset.Support.ToString("G29", CultureInfo.InvariantCulture),
            asset.RiskReward.ToString("G29", CultureInfo.InvariantCulture),
            asset.VolumeSpike.ToString("G29", CultureInfo.InvariantCulture),
            asset.Rsi.ToString("G29", CultureInfo.InvariantCulture),
            asset.Adx.ToString("G29", CultureInfo.InvariantCulture),
            asset.MarketRegime);

    private async Task AnalyzeTopRankingWithLlmAsync(ScanProfile profile, IReadOnlyList<AssetScore> ranking)
    {
        if (chkAutoLlmTop30?.IsChecked != true || ranking.Count == 0)
            return;

        if (!_automaticLlmProfilesRunning.TryAdd(profile.Name, 0))
            return;

        try
        {
            var top = ranking
                .OrderByDescending(asset => asset.OpportunityScore)
                .ThenByDescending(asset => asset.Score)
                .Take(AutomaticLlmTopCount)
                .ToArray();

            var previousTop = _automaticLlmTopSymbols.GetValueOrDefault(profile.Name) ?? [];
            _automaticLlmTopSymbols[profile.Name] = top
                .Select(asset => asset.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in top)
            {
                var key = $"{profile.Name}|{asset.Symbol}";
                var signature = BuildLlmAnalysisSignature(asset);
                var enteredTop = !previousTop.Contains(asset.Symbol);

                if (!enteredTop &&
                    _automaticLlmLastSignature.TryGetValue(key, out var lastSignature) &&
                    string.Equals(lastSignature, signature, StringComparison.Ordinal))
                    continue;

                try
                {
                    await _automaticLlmGate.WaitAsync(_labClosed.Token);
                    try
                    {
                        var opinion = await _llmAnalyzer.AnalyzeAsync(
                            null,
                            BuildLlmIndicators(asset),
                            _labClosed.Token);

                        await _llmOpinionRepository.AddAsync(new LlmOpinionRecord
                        {
                            CreatedAt = DateTime.Now,
                            Symbol = asset.Symbol,
                            Profile = profile.Name,
                            ImagePath = "",
                            AnalysisPrice = asset.Close,
                            ScannerSignal = asset.DisplaySignal,
                            Decision = opinion.Decisao,
                            Direction = opinion.Direcao,
                            Confidence = opinion.Confianca,
                            Trend = opinion.Tendencia,
                            Entry = opinion.Entrada,
                            Stop = opinion.Stop,
                            Tp1 = opinion.Tp1,
                            Tp2 = opinion.Tp2,
                            Reasons = opinion.Motivos.Length == 0 ? "" : string.Join("; ", opinion.Motivos),
                            Risks = opinion.Riscos.Length == 0 ? "" : string.Join("; ", opinion.Riscos)
                        });

                        _automaticLlmLastSignature[key] = signature;

                        if (profile.Name == _viewedProfile.Name)
                        {
                            txtLlmOpinion.Text = FormatLlmOpinion(asset, opinion);
                            txtLlmOpinion.ToolTip = $"Análise automática salva no histórico: {asset.Symbol}.";
                        }
                    }
                    finally
                    {
                        _automaticLlmGate.Release();
                    }
                }
                catch (OperationCanceledException) when (_labClosed.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (profile.Name == _viewedProfile.Name)
                        txtLlmOpinion.ToolTip = $"Falha na análise automática de {asset.Symbol}: {ex.Message}";
                }
            }
        }
        finally
        {
            _automaticLlmProfilesRunning.TryRemove(profile.Name, out _);
        }
    }
    private void BtnCopyLlmOpinion_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtLlmOpinion.Text))
            return;

        System.Windows.Clipboard.SetText(txtLlmOpinion.Text);
        txtLlmOpinion.ToolTip = "Análise copiada para a área de transferência.";
    }

    private async Task RecordLabGridAsync(ScanProfile profile)
    {
        if(dgRanking.ItemsSource is not IEnumerable<AssetScore> rows)return;
        try { await _lab.ObserveGridAsync(rows.ToArray(),profile,_labClosed.Token); }
        catch(OperationCanceledException) when(_labClosed.IsCancellationRequested) { }
        catch(Exception ex){_labStatus=$"Falha no laboratório: {ex.Message}";RefreshDiagnosticsDisplay();}
    }

    private async Task EvaluateLabAsync()
    {
        try { await _lab.EvaluateAsync(_labClosed.Token); await _scanner.EvaluateSignalExecutionsAsync(_labClosed.Token); }
        catch(OperationCanceledException) when(_labClosed.IsCancellationRequested) { }
        catch(Exception ex){_labStatus=$"Falha no laboratório: {ex.Message}";RefreshDiagnosticsDisplay();}
    }

    private async Task DispatchAlertsAsync(IReadOnlyList<NewSignalAlert> newSignals)
    {
        if (newSignals.Count == 0)
            return;

        AlertSettings settings;
        try
        {
            await _alertSettingsRepository.InitializeAsync();
            settings = await _alertSettingsRepository.LoadAsync();
        }
        catch
        {
            return; // não deixa falha de configuração derrubar o scan
        }

        string title = newSignals.Count == 1
            ? $"Novo sinal: {newSignals[0].Symbol}"
            : $"{newSignals.Count} novos sinais";

        string body = string.Join("\n", newSignals.Select(s =>
            $"{s.Symbol} — {s.Signal} | Score {s.Score:F2} | Preço {s.Price} | {s.Profile}"));

        if (settings.DesktopEnabled && _trayIcon != null)
        {
            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = body.Length > 250 ? body[..250] + "..." : body;
            _trayIcon.ShowBalloonTip(5000);
        }

        var channels = AlertChannelFactory.BuildEnabledChannels(settings);
        if (channels.Count > 0)
        {
            var dispatcher = new AlertDispatcher(channels);
            await dispatcher.SendAsync(title, body); // falhas de canal individual não travam o app
        }
    }

    // Atualização manual: dispara os 2 perfis, igual ao timer.
    private void btAtualizar_Click(object sender, RoutedEventArgs e)
    {
        _ = RunScannerAsync(ScanProfile.Swing);
        _ = RunScannerAsync(ScanProfile.Intraday);
    }

    private void TxtSearchSymbol_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            BtnSearch_Click(sender, e);
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        string input = txtSearchSymbol.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!input.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            input += "USDT";

        btnSearch.IsEnabled = false;
        try
        {
            // Busca no lado selecionado; no modo Ambos, mostra as duas leituras
            // do mesmo ativo no ranking.
            var lookupResults = await Task.WhenAll(GetSelectedScannerDirections().Select(direction =>
                _scanner.LookupSymbolAsync(
                    input,
                    _viewedProfile,
                    direction: direction,
                    shortExperimental: direction == TradeDirection.Short && GetUseShortExperimentalProfile())));
            var foundResults = lookupResults.Where(result => result != null).Cast<AssetScore>().ToList();
            if (foundResults.Count == 0)
            {
                MessageBox.Show(
                    $"Não foi possível encontrar dados para \"{input}\".\nVerifique o símbolo (ex.: DOGEUSDT).",
                    "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove as leituras antigas do mesmo símbolo e insere as novas no topo,
            // dentro do ranking do perfil exibido.
            var currentRanking = _rankingsByProfile.GetValueOrDefault(_viewedProfile.Name, Array.Empty<AssetScore>());
            var updated = currentRanking
                .Where(a => !string.Equals(a.Symbol, input, StringComparison.OrdinalIgnoreCase))
                .ToList();
            updated.InsertRange(0, foundResults);
            _rankingsByProfile[_viewedProfile.Name] = updated;

            // Se o filtro "só favoritos" estiver ativo e a moeda buscada não for favorita,
            // desativa o filtro pra garantir que o resultado apareça — senão o clique em
            // "Buscar" pareceria não ter feito nada.
            if (chkFavoritesOnly.IsChecked == true && foundResults.All(result => !result.IsFavorite))
                chkFavoritesOnly.IsChecked = false;

            ApplyRankingFilter();
            dgRanking.SelectedItem = foundResults[0];
            dgRanking.ScrollIntoView(foundResults[0]);
            _=RecordLabGridAsync(_viewedProfile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao buscar o símbolo.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnSearch.IsEnabled = true;
        }
    }

    private void BtnSimulateTrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not AssetScore asset)
            return;

        // O trade simulado herda o perfil que está sendo exibido (a linha clicada
        // pertence ao grid do perfil exibido).
        var window = new SimulateTradeWindow(_simulatedTradeRepository, asset, _viewedProfile.Name,
            () => _priceCheckService.GetCurrentPriceAsync(asset.Symbol))
        {
            Owner = this
        };
        window.ShowDialog();

        if (window.Saved)
        {
            if (window.SavedTradeId is int tradeId)
                _ = LinkLatestLlmOpinionAsync(tradeId, asset, _viewedProfile.Name);
            _ = LoadSimulatedTradesAsync();
        }
    }

    private async Task LoadSimulatedTradesAsync()
    {
        try
        {
            await _simulatedTradeRepository.InitializeAsync();
            var trades = await _simulatedTradeRepository.GetAllAsync();

            // Pros trades ainda abertos, busca o preço atual e calcula o P/L não-realizado.
            // Trades fechados não precisam disso — já têm o resultado final gravado.
            foreach (var trade in trades.Where(t => !t.Closed))
            {
                try
                {
                    decimal currentPrice = await _priceCheckService.GetCurrentPriceAsync(trade.Symbol);
                    trade.CurrentPrice = currentPrice;
                    trade.UnrealizedPnLPercent = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - currentPrice : currentPrice - trade.EntryPrice) / trade.EntryPrice * 100m;
                }
                catch
                {
                    // Símbolo com erro momentâneo — deixa em branco, tenta de novo na próxima atualização.
                }
            }

            _lastSimulatedTrades = trades;
            ApplySimulatedTradesFilter();

            int totalClosed = trades.Count(t => t.Closed);
            int wins = trades.Count(t => t.Closed && t.OutcomePercent > 0);
            double winRate = totalClosed > 0 ? wins * 100.0 / totalClosed : 0;
            decimal totalReturn = trades.Where(t => t.Closed).Sum(t => t.OutcomePercent ?? 0);
            int openCount = trades.Count(t => !t.Closed);

            txtSimulatedSummary.Text = $"Trades: {trades.Count} total ({openCount} em aberto, {totalClosed} fechados)   |   " +
                                        $"Win Rate: {winRate:F1}%   |   Retorno Acumulado: {totalReturn:F2}%";

            txtDashboardSimulated.Text = trades.Count == 0
                ? "Nenhum trade registrado"
                : $"{openCount} aberto(s) | Win Rate {winRate:F1}% | Retorno {totalReturn:F2}%";

            await SyncWebSocketSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o diário de trades.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Assina a UNIÃO dos símbolos dos 2 rankings — não só do perfil exibido, senão o
    // preço em tempo real do perfil "escondido" para de atualizar.
    private async Task SyncWebSocketSubscriptionsAsync()
    {
        // BTCUSDT sempre inscrito, independente de estar no ranking — o Dashboard precisa
        // do preço dele sempre disponível.
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BTCUSDT" };

        foreach (var ranking in _rankingsByProfile.Values)
            foreach (var asset in ranking)
                desired.Add(asset.Symbol);

        foreach (var trade in _lastSimulatedTrades.Where(t => t.IsOpen))
            desired.Add(trade.Symbol);

        try
        {
            await _webSocketService.SyncSubscriptionsAsync(desired);
        }
        catch
        {
            // Falha ao sincronizar inscrições não deve travar o app — tenta de novo
            // automaticamente na próxima vez que o Diário for recarregado.
        }
    }

    /// <summary>
    /// Processa um preço novo pra um trade simulado, seguindo TP1→TP2→breakeven→TP3
    /// — ou o fechamento único de sempre,
    /// se o trade não tiver TP1 (modo de risco antigo, ou trade criado antes dessa etapa).
    /// A parte que mexe em objetos ligados à UI roda dentro de Dispatcher.Invoke (seguro tanto
    /// se já estivermos na thread da UI — caso do scan — quanto vindo de outra thread — caso
    /// do WebSocket); a parte de I/O (persistir no banco) fica sempre fora do Invoke.
    /// Devolve true se o trade foi fechado por completo.
    /// Sem mudança nesse método — já era Profile-agnóstico (cada trade guarda o próprio
    /// Profile e o preço chega via WebSocket independente de qual perfil está sendo exibido).
    /// </summary>
    private async Task<bool> ProcessPartialExitTickAsync(SimulatedTrade trade, decimal price)
    {
        string? closeReason = null;
        decimal closeExitPrice = 0;
        decimal closeOutcome = 0;
        bool partialHit = false;

        Dispatcher.Invoke(() =>
        {
            if (trade.TakeProfit1 == null)
            {
                // Sem TP1 — comportamento original, fechamento único.
                bool stopHit = trade.Direction == TradeDirection.Short ? price >= trade.StopLoss : price <= trade.StopLoss;
                bool targetHit = trade.Direction == TradeDirection.Short ? price <= trade.TakeProfit : price >= trade.TakeProfit;
                if (stopHit)
                {
                    closeReason = "SL";
                    closeExitPrice = trade.StopLoss;
                    closeOutcome = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - trade.StopLoss : trade.StopLoss - trade.EntryPrice) / trade.EntryPrice * 100m;
                }
                else if (targetHit)
                {
                    closeReason = "TP";
                    closeExitPrice = trade.TakeProfit;
                    closeOutcome = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - trade.TakeProfit : trade.TakeProfit - trade.EntryPrice) / trade.EntryPrice * 100m;
                }
                return;
            }

            if (trade.Direction == TradeDirection.Short)
            {
                if (price >= trade.StopLoss)
                {
                    decimal legReturn = (trade.EntryPrice - trade.StopLoss) / trade.EntryPrice * 100m;
                    closeOutcome = trade.WeightedExitSum + trade.RemainingFraction * legReturn;
                    closeReason = trade.Tp1Hit ? (trade.Tp2Hit ? "TP1TP2SL" : "TP1SL") : "SL";
                    closeExitPrice = trade.StopLoss;
                    return;
                }

                if (!trade.Tp1Hit && price <= trade.TakeProfit1.Value)
                {
                    const decimal tp1Fraction = 0.40m;
                    decimal legReturn = (trade.EntryPrice - trade.TakeProfit1.Value) / trade.EntryPrice * 100m;
                    trade.WeightedExitSum += tp1Fraction * legReturn;
                    trade.RemainingFraction -= tp1Fraction;
                    trade.Tp1Hit = true;
                    partialHit = true;
                }

                if (trade.Tp1Hit && !trade.Tp2Hit && price <= trade.TakeProfit)
                {
                    const decimal tp2Fraction = 0.40m;
                    decimal legReturn = (trade.EntryPrice - trade.TakeProfit) / trade.EntryPrice * 100m;
                    trade.WeightedExitSum += tp2Fraction * legReturn;
                    trade.RemainingFraction -= tp2Fraction;
                    trade.Tp2Hit = true;
                    trade.StopLoss = Math.Min(trade.StopLoss, trade.EntryPrice);
                    partialHit = true;
                }

                if (trade.Tp2Hit && trade.TakeProfit3.HasValue && price <= trade.TakeProfit3.Value)
                {
                    decimal legReturn = (trade.EntryPrice - trade.TakeProfit3.Value) / trade.EntryPrice * 100m;
                    closeOutcome = trade.WeightedExitSum + trade.RemainingFraction * legReturn;
                    closeReason = "TP1TP2TP3";
                    closeExitPrice = trade.TakeProfit3.Value;
                }
                return;
            }

            // 1. Stop Loss sempre tem prioridade — pode estar na entrada após o TP2.
            if (price <= trade.StopLoss)
            {
                decimal legReturn = (trade.StopLoss - trade.EntryPrice) / trade.EntryPrice * 100m;
                closeOutcome = trade.WeightedExitSum + trade.RemainingFraction * legReturn;
                closeReason = trade.Tp1Hit ? (trade.Tp2Hit ? "TP1TP2SL" : "TP1SL") : "SL";
                closeExitPrice = trade.StopLoss;
                return;
            }

            // 2. TP1 — realiza 40% e mantém o stop original.
            if (!trade.Tp1Hit && price >= trade.TakeProfit1.Value)
            {
                const decimal tp1Fraction = 0.40m;
                decimal legReturn = (trade.TakeProfit1.Value - trade.EntryPrice) / trade.EntryPrice * 100m;

                trade.WeightedExitSum += tp1Fraction * legReturn;
                trade.RemainingFraction -= tp1Fraction;
                trade.Tp1Hit = true;
                partialHit = true;
            }

            // 3. TP2 — a resistência estrutural (TakeProfit "principal" de sempre). Realiza mais 40%.
            if (trade.Tp1Hit && !trade.Tp2Hit && price >= trade.TakeProfit)
            {
                const decimal tp2Fraction = 0.40m;
                decimal legReturn = (trade.TakeProfit - trade.EntryPrice) / trade.EntryPrice * 100m;

                trade.WeightedExitSum += tp2Fraction * legReturn;
                trade.RemainingFraction -= tp2Fraction;
                trade.Tp2Hit = true;
                trade.StopLoss = Math.Max(trade.StopLoss, trade.EntryPrice);
                partialHit = true;
            }

            // 4. TP3 — fecha o restante da posição.
            if (trade.Tp2Hit && trade.TakeProfit3.HasValue && price >= trade.TakeProfit3.Value)
            {
                decimal legReturn = (trade.TakeProfit3.Value - trade.EntryPrice) / trade.EntryPrice * 100m;
                closeOutcome = trade.WeightedExitSum + trade.RemainingFraction * legReturn;
                closeReason = "TP1TP2TP3";
                closeExitPrice = trade.TakeProfit3.Value;
            }
        });

        if (closeReason != null)
        {
            try
            {
                await _simulatedTradeRepository.CloseTradeAsync(trade.Id, closeExitPrice, closeOutcome, closeReason);
                await RecordLlmOutcomeAsync(trade.Id, closeOutcome, closeReason, DateTime.UtcNow);
            }
            catch
            {
                return false; // falha ao persistir — não marca como fechado, tenta de novo depois
            }

            Dispatcher.Invoke(() =>
            {
                trade.ExitPrice = closeExitPrice;
                trade.OutcomePercent = closeOutcome;
                trade.ExitReason = closeReason;
                trade.ExitTime = DateTime.UtcNow;
                trade.Closed = true; // já notifica IsOpen junto
            });

            return true;
        }

        if (partialHit)
        {
            try
            {
                await _simulatedTradeRepository.UpdatePartialExitStateAsync(
                    trade.Id, trade.Tp1Hit, trade.Tp2Hit, trade.RemainingFraction, trade.WeightedExitSum, trade.StopLoss);
            }
            catch
            {
                // Estado em memória já mudou e já notificou a UI — só a persistência falhou;
                // tenta salvar de novo no próximo tick/scan.
            }
        }

        return false;
    }

    // Procura o símbolo atualizado nos 2 rankings (não só no exibido) — o preço fica
    // fresco em ambos, mesmo no perfil "escondido", pra quando o usuário trocar de rádio.
    private async void OnWebSocketPriceUpdated(string symbol, decimal price)
    {
        var matchingTrades = new List<SimulatedTrade>();

        Dispatcher.Invoke(() =>
        {
            if (string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase))
                txtDashboardBtcPrice.Text = price.ToString("N2");

            foreach (var ranking in _rankingsByProfile.Values)
            {
                var matchedAsset = ranking.FirstOrDefault(a => string.Equals(a.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                if (matchedAsset != null)
                    matchedAsset.LivePrice = price;
            }

            foreach (var trade in _lastSimulatedTrades.Where(t =>
                         t.IsOpen && string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            {
                trade.CurrentPrice = price;
                trade.UnrealizedPnLPercent = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - price : price - trade.EntryPrice) / trade.EntryPrice * 100m;
                matchingTrades.Add(trade);
            }
        });

        foreach (var trade in matchingTrades)
            await ProcessPartialExitTickAsync(trade, price);
    }

    /// <summary>
    /// Reage ao fechamento de um candle de BTCUSDT — dispara o scan do PERFIL
    /// CORRESPONDENTE ao timeframe que fechou (1h → Intraday, 4h → Swing), não mais
    /// só "o perfil selecionado na tela". O timer continua existindo como rede de
    /// segurança (se esse evento se perder por qualquer motivo, o scan roda de
    /// qualquer jeito, só um pouco mais tarde).
    /// </summary>
    private void OnCandleClosed(string interval)
    {
        ScanProfile? matching =
            string.Equals(interval, ScanProfile.Swing.CandleInterval, StringComparison.OrdinalIgnoreCase) ? ScanProfile.Swing :
            string.Equals(interval, ScanProfile.Intraday.CandleInterval, StringComparison.OrdinalIgnoreCase) ? ScanProfile.Intraday :
            null;

        if (matching == null)
            return; // não é 1h nem 4h — não deveria acontecer, únicos streams assinados

        // Esse evento chega pela thread de recebimento do WebSocket, não pela thread da UI —
        // RunScannerAsync mexe direto em elementos de tela, então precisa ser despachado.
        Dispatcher.BeginInvoke(async () => await RunScannerAsync(matching));
    }

    private async void DgSimulatedTrades_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        if (e.Row.Item is not SimulatedTrade trade)
            return;

        if (trade.Closed)
        {
            // Edição em trade já fechado não faz sentido — a próxima atualização do
            // diário vai reverter visualmente pro valor real gravado no banco.
            return;
        }

        try
        {
            // WPF já aplicou os valores editados no objeto (TakeProfit/StopLoss/Note)
            // antes desse evento disparar, então só precisamos persistir.
            await _simulatedTradeRepository.UpdateTradeDetailsAsync(trade.Id, trade.TakeProfit, trade.StopLoss, trade.Note);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar a alteração.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRefreshSimulatedTrades_Click(object sender, RoutedEventArgs e) => await LoadSimulatedTradesAsync();

    private void DgSimulatedTrades_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // O WPF não seleciona a linha sozinho com clique direito (só clique esquerdo) —
        // sem isso, "Copiar linha" poderia copiar uma linha diferente da que foi clicada.
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null)
            row.IsSelected = true;
    }

    private void BtnCopySimulatedTradeRow_Click(object sender, RoutedEventArgs e)
    {
        if (dgSimulatedTrades.SelectedItem is not SimulatedTrade trade)
            return;

        string status = trade.IsOpen ? "Aberto" : $"Fechado ({trade.ExitReason})";
        string currentOrExit = trade.IsOpen
            ? $"Preço Atual: {trade.CurrentPrice?.ToString("0.########") ?? "—"} | P/L: {trade.UnrealizedPnLPercent?.ToString("F2") ?? "—"}%"
            : $"Preço Saída: {trade.ExitPrice?.ToString("0.########") ?? "—"} | Resultado: {trade.OutcomePercent?.ToString("F2") ?? "—"}%";

        string text = $"{trade.Symbol} | Entrada: {trade.EntryPrice:0.########} em {trade.EntryTime:dd/MM/yyyy HH:mm} | " +
                      $"TP1: {trade.TakeProfit1?.ToString("0.########") ?? "—"} | TP2: {trade.TakeProfit:0.########} | TP3: {trade.TakeProfit3?.ToString("0.########") ?? "—"} | " +
                      $"SL: {trade.StopLoss:0.########} | {currentOrExit} | " +
                      $"Progresso: {trade.PartialExitProgressText} | Status: {status}" +
                      (string.IsNullOrWhiteSpace(trade.Note) ? "" : $" | Obs: {trade.Note}");

        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível copiar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnCloseSimulatedTradeRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not SimulatedTrade trade)
            return;

        if (trade.Closed)
            return; // já fechado — não deveria nem aparecer o botão, mas por segurança

        var confirm = MessageBox.Show($"Fechar manualmente o trade de {trade.Symbol}?", "CryptoScanner", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            decimal currentPrice = await _priceCheckService.GetCurrentPriceAsync(trade.Symbol);

            // Se já teve saída parcial (TP1/TP2 batidos), o resultado final precisa ponderar
            // o que já foi realizado com a fração restante fechando agora — mesma lógica do
            // Backtest. Sem TP1 (trade antigo ou modo sem saída parcial), fecha tudo de uma vez.
            decimal outcomePercent;
            if (trade.TakeProfit1 != null && (trade.Tp1Hit || trade.Tp2Hit))
            {
                decimal legReturn = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - currentPrice : currentPrice - trade.EntryPrice) / trade.EntryPrice * 100m;
                outcomePercent = trade.WeightedExitSum + trade.RemainingFraction * legReturn;
            }
            else
            {
                outcomePercent = (trade.Direction == TradeDirection.Short ? trade.EntryPrice - currentPrice : currentPrice - trade.EntryPrice) / trade.EntryPrice * 100m;
            }

            await _simulatedTradeRepository.CloseTradeAsync(trade.Id, currentPrice, outcomePercent, "Manual");
            await RecordLlmOutcomeAsync(trade.Id, outcomePercent, "Manual", DateTime.UtcNow);
            await LoadSimulatedTradesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível fechar o trade.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EvaluateSimulatedTradesAsync()
    {
        IReadOnlyList<SimulatedTrade> openTrades;
        try
        {
            await _simulatedTradeRepository.InitializeAsync();
            openTrades = await _simulatedTradeRepository.GetOpenAsync();
        }
        catch
        {
            return; // não deixa falha de leitura derrubar o scan
        }

        foreach (var trade in openTrades)
        {
            try
            {
                decimal currentPrice = await _priceCheckService.GetCurrentPriceAsync(trade.Symbol);
                await ProcessPartialExitTickAsync(trade, currentPrice);
            }
            catch
            {
                // símbolo com erro momentâneo — tenta de novo no próximo scan
            }
        }
        // Quem chama esse método (RunScannerAsync) já recarrega o Diário logo em seguida,
        // então não precisamos disparar isso aqui — evita duplicar a chamada.
    }

    // Lê do dicionário — perfil que está sendo EXIBIDO agora, não necessariamente
    // o que acabou de terminar de escanear.
    private void ApplyRankingFilter()
    {
        var ranking = _rankingsByProfile.GetValueOrDefault(_viewedProfile.Name, Array.Empty<AssetScore>());
        dgRanking.ItemsSource = chkFavoritesOnly.IsChecked == true
            ? ranking.Where(a => a.IsFavorite).ToList()
            : ranking;
        RefreshDiagnosticsDisplay();
    }

    private void ApplySimulatedTradesFilter()
    {
        dgSimulatedTrades.ItemsSource = _showClosedTrades
            ? _lastSimulatedTrades
            : _lastSimulatedTrades.Where(t => t.IsOpen).ToList();
    }

    private void BtnToggleClosedTrades_Click(object sender, RoutedEventArgs e)
    {
        _showClosedTrades = !_showClosedTrades;
        btnToggleClosedTrades.Content = _showClosedTrades ? "Ocultar Concluídos" : "Mostrar Concluídos";
        ApplySimulatedTradesFilter();
    }

    // Combina os 2 perfis pro Top Candidatos — cada item guarda de qual perfil veio,
    // porque o mesmo símbolo pode aparecer elegível nos 2 ao mesmo tempo.
    private void UpdateDashboard(string marketRegime)
    {
        txtDashboardRegime.Text = marketRegime;
        borderRegime.Background = marketRegime switch
        {
            "BULL" => Brushes.SeaGreen,
            "BEAR" => Brushes.IndianRed,
            _ => Brushes.Goldenrod // LATERAL ou qualquer outro valor inesperado
        };

        var eligible = _rankingsByProfile
            .SelectMany(kvp => kvp.Value.Where(a => a.IsEligible).Select(a => (Profile: kvp.Key, Asset: a)))
            .OrderByDescending(x => x.Asset.Score)
            .ToList();

        txtDashboardEligible.Text = eligible.Count.ToString();

        txtDashboardTopCandidates.Text = eligible.Count == 0
            ? "Nenhum no momento"
            : string.Join("  |  ", eligible.Take(3).Select(x => $"{x.Asset.Symbol} ({x.Asset.Score:F1}, {x.Profile})"));
    }

    private bool _btcDominanceErrorShown; // diagnóstico temporário — remove depois de achar a causa

    private async Task UpdateBtcDominanceAsync()
    {
        var (dominance, error) = await _coinGeckoService.GetBitcoinDominanceAsync();

        if (dominance.HasValue)
        {
            txtDashboardBtcDominance.Text = $"{dominance.Value:F1}%";
            return;
        }

        txtDashboardBtcDominance.Text = "—";

        if (!_btcDominanceErrorShown)
        {
            _btcDominanceErrorShown = true;
            MessageBox.Show($"Diagnóstico (só aparece 1 vez): falha ao buscar dominância do BTC.\n{error}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChkFavoritesOnly_Changed(object sender, RoutedEventArgs e) => ApplyRankingFilter();

    private async void ChkFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkbox || checkbox.DataContext is not AssetScore asset)
            return;

        bool newState = checkbox.IsChecked == true;
        asset.IsFavorite = newState;

        try
        {
            if (newState)
                await _watchlistRepository.AddAsync(asset.Symbol);
            else
                await _watchlistRepository.RemoveAsync(asset.Symbol);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível atualizar a watchlist.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Se o filtro "só favoritos" estiver ativo e o usuário desmarcar uma moeda,
        // ela precisa sumir da lista imediatamente.
        if (chkFavoritesOnly.IsChecked == true && !newState)
            ApplyRankingFilter();
    }

    private void DgRanking_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dgRanking.SelectedItem is not AssetScore asset)
            return;

        CopyAssetQualityToClipboard(asset);

        // Clicar na mesma linha que já está aberta fecha o popup (comportamento de alternância).
        if (popupBreakdown.IsOpen && ReferenceEquals(popupBreakdown.DataContext, asset))
        {
            popupBreakdown.IsOpen = false;
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        popupBreakdown.DataContext = asset;
        popupBreakdown.PlacementTarget = (UIElement?)row ?? dgRanking;
        popupBreakdown.Placement = PlacementMode.Bottom;
        popupBreakdown.IsOpen = true;
    }

    private void DgRankingRow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is DataGridRow row && ReferenceEquals(popupBreakdown.DataContext, row.Item))
            popupBreakdown.IsOpen = false;
    }

    private static void CopyAssetQualityToClipboard(AssetScore asset)
    {
        string text = $"{asset.Symbol} | Lado: {(asset.Direction == TradeDirection.Short ? "VENDA" : "COMPRA")} | Preço: {asset.CloseFormatted} | Score: {asset.Score:F2} | " +
                      $"Elegível: {(asset.IsEligible ? "Sim" : "Não")} | Sinal: {asset.DisplaySignal} | Elite: {(asset.IsEliteSetup ? "Sim" : "Não")} | " +
                      $"Var: {asset.VariationText} | Trend: {asset.TrendDirection} | RR: {asset.RiskReward:F2} | " +
                      $"Res %: {asset.ResistanceDistance:F1} | Sup %: {asset.SupportDistance:F1} | Vol Spike: {asset.VolumeSpike:F2} | " +
                      $"Força Rel.: {asset.RelativeStrengthText} | Consol.: {(asset.IsConsolidating ? "Sim" : "Não")} | " +
                      $"Exaustão: {(asset.HasExhaustion ? "Sim" : "Não")} | Padrão: {asset.PatternName} | Smart Money: {asset.SmartMoneyLabel}" +
                      (asset.IsBullTrap ? " | ⚠ BULL TRAP" : "") +
                      (!string.IsNullOrEmpty(asset.PartialExitTargetsText) ? $" | {asset.PartialExitTargetsText}" : "") +
                      $"\n\n{asset.QualityAnalysis}";

        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // Falha ao copiar (ex.: clipboard ocupado por outro processo) não deve
            // impedir o popup de abrir normalmente — só perde a cópia dessa vez.
        }
    }

    private void DgRanking_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not AssetScore asset)
            return;

        // Usa o perfil exibido agora pra escolher o intervalo do gráfico.
        string interval = ToTradingViewInterval(_viewedProfile.CandleInterval);
        var chartWindow = new ChartWindow(asset.Symbol, interval) { Owner = this };
        chartWindow.Show();
    }

    private static string ToTradingViewInterval(string candleInterval) => candleInterval switch
    {
        "1h" => "60",
        "4h" => "240",
        "1d" => "D",
        _ => "240" // não reconhecido — cai num padrão razoável em vez de quebrar
    };

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static string GetDatabasePath()
    {
        string databaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CryptoScanner");
        Directory.CreateDirectory(databaseDirectory);
        string databasePath = Path.Combine(databaseDirectory, "signals.db");
        string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signals.db");
        if (!File.Exists(databasePath) && File.Exists(legacyPath))
            File.Copy(legacyPath, databasePath);
        return databasePath;
    }

    // --- Novos métodos auxiliares do Scan Duplo -------------------------------

    private static (string Label, int Count)[] Blockers(FilterDiagnostics d) => new[]
    {
        ("Risco/retorno insuficiente",d.FailedRiskReward),("Níveis inválidos",d.FailedInvalidLevels),("Volume insuficiente",d.FailedVolumeSpike),
        ("Sem caminho de entrada confirmado",d.FailedBreakout),("Sem consolidação prévia",d.FailedConsolidation),
        ("Alvo próximo demais",d.FailedResistanceDistance),("Tendência incompatível",d.FailedDirection),
        ("Score insuficiente",d.FailedScore),("Stop distante demais",d.FailedStopDistanceTooHigh),
        ("Stop próximo demais",d.FailedStopDistance),("R/R acima do teto",d.FailedRiskRewardTooHigh),
        ("Armadilha de alta",d.FailedBullTrap),("Confirmação EMA",d.FailedTrendConfirmation),
        ("Momentum",d.FailedMomentumFilter),("Score Short máx.",d.FailedShortScoreCeiling),("ADX Short BEAR máx.",d.FailedShortAdxInBear),("Regime de reversão à média",d.FailedMeanReversionRegimeFilter),
        ("ATR de reversão à média",d.FailedMeanReversionAtrFilter)
    }.OrderByDescending(x=>x.Item2).ToArray();

    private void RefreshDiagnosticsDisplay()
    {
        if(txtScanSummary is null || txtDiagnostics is null)return;
        string profile=_viewedProfile.Name;
        var ranking=_rankingsByProfile.GetValueOrDefault(profile,Array.Empty<AssetScore>());
        var diag=_diagnosticsByProfile.GetValueOrDefault(profile);
        string updated=_scanCompletedAt.TryGetValue(profile,out var at)?at.ToString("dd/MM HH:mm:ss"):"aguardando";
        string side = IsBothDirectionsSelected() ? "Ambos" : GetSelectedScannerDirection() == TradeDirection.Short ? "Venda" : "Compra";
        string experiment = GetUseShortExperimentalProfile() ? " · Short experimental" : "";
        txtScanSummary.Text=$"{profile} · {side}{experiment} · Analisados: {diag?.TotalAnalyzed ?? 0} · Elegíveis no universo: {diag?.PassedAll ?? 0} · Em observação: {ranking.Count(a=>a.DisplaySignal=="MONITORAR")} · Atualizado: {updated}";
        txtScanSummary.ToolTip="Resumo do perfil completo, antes do filtro de favoritos. A busca manual não muda o horário da última varredura.";
        var top=diag is null?default:Blockers(diag).First();
        txtDiagnostics.Text=diag is null?"Aguardando análise deste perfil.":top.Count>0
            ?$"Bloqueio mais frequente: {top.Label} — {top.Count} de {diag.TotalAnalyzed} ativos."
            :"Nenhum bloqueio registrado nos filtros desta varredura.";
        if(_pressureHistoryErrors>0)txtDiagnostics.Text+=" Atenção: falha na gravação do histórico; veja os motivos.";
        if(_labStatus.StartsWith("Falha"))txtDiagnostics.Text+=" Atenção: falha no laboratório; veja os motivos.";
    }

    private void BtnScanReasons_Click(object sender,RoutedEventArgs e)
    {
        var lines=new List<string>{"Um ativo pode falhar em vários filtros; não some os contadores.","Consolidação é obrigatória em BULL; em BEAR/LATERAL é informativa.",""};
        foreach(string profile in new[]{_viewedProfile.Name, _viewedProfile.Name==ScanProfile.Swing.Name?ScanProfile.Intraday.Name:ScanProfile.Swing.Name})
        {
            lines.Add(profile);
            if(_diagnosticsByProfile.GetValueOrDefault(profile) is { } d)
            {
                lines.Add($"Consultadas: {d.Requested} | Válidas: {d.TotalAnalyzed} | Erros: {d.Errors.Count} | Elegíveis: {d.PassedAll} | Gravados: {d.SignalsSaved} | Repetidos: {d.SkippedDuplicateToday}");
                lines.Add("Candidatos: " + string.Join(" | ",d.CandidateTypes.Select(x=>$"{x.Key}: {x.Value}")));
                lines.Add("Passariam removendo somente um filtro (diagnóstico, não recomendação):");
                lines.AddRange(d.OnlyBlockedBy.Select(x=>$"{x.Key}: {string.Join(", ",x.Value)}"));
                lines.AddRange(d.Errors.Select(x=>$"Erro em {x.Key}: {x.Value}"));
                lines.AddRange(Blockers(d).Select(x=>$"{x.Label}: {x.Count}"));
            }
            else lines.Add("Ainda sem análise nesta sessão.");
            lines.Add("");
        }
        lines.Add(_pressureHistoryErrors==0?"Histórico da pressão: gravação ativa.":$"Histórico da pressão: {_pressureHistoryErrors} falha(s) — {_lastPressureHistoryError}");
        lines.Add(_labStatus);
        new Window{Owner=this,Title="Motivos e estado do scanner",Width=650,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new System.Windows.Controls.TextBox{Text=string.Join("\n",lines),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,
                VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,Margin=new Thickness(12),Padding=new Thickness(8)}}.ShowDialog();
    }
    private void ReportPressureHistoryError(string message)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _pressureHistoryErrors++;
            _lastPressureHistoryError = message;
            RefreshDiagnosticsDisplay();
        }));
    }

    // Centraliza o texto do título — antes ficava embutido em RunScannerAsync, agora
    // ProfileChanged também precisa remontá-lo sem re-escanear.
    // Simplificação em relação ao original: tirei WinRate/Avg do título (já aparecem em
    // txtWinRate/txtAvgReturn na tela); se quiser manter, adicione de volta usando os
    // valores já calculados em RunScannerAsync.
    private void RefreshTitle()
    {
        string experiment = GetUseShortExperimentalProfile() ? " | Short experimental" : "";
        string side = IsBothDirectionsSelected() ? "Ambos" : GetSelectedScannerDirection() == TradeDirection.Short ? "Venda" : "Compra";
        Title = $"Scanner [{_lastMarketRegime}] | {side}{experiment} | Exibindo: {_viewedProfile.Name}";
    }
}
