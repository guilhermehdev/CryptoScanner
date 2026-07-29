using CryptoScanner.Application.Services;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Infrastructure.Sqlite;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace CryptoScanner.UI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly ScannerService _scanner;
    private readonly IWatchlistRepository _watchlistRepository;
    private readonly IAlertSettingsRepository _alertSettingsRepository;
    private bool _isScanning;
    private bool _isWindowLoaded;
    private ScanProfile _currentProfile = ScanProfile.Swing;
    private IReadOnlyList<SignalHistory> _lastHistory = Array.Empty<SignalHistory>();
    private IReadOnlyList<AssetScore> _lastRanking = Array.Empty<AssetScore>();
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        var databasePath = GetDatabasePath();
        _watchlistRepository = new SqliteWatchlistRepository(databasePath);
        _alertSettingsRepository = new SqliteAlertSettingsRepository(databasePath);

        _scanner = new ScannerService(
            new BinanceExchangeService(),
            new SqliteSignalRepository(databasePath),
            _watchlistRepository,
            new AssetAnalyzer());

        Loaded += MainWindow_Loaded;
        _timer.Interval = TimeSpan.FromMinutes(30); // perfil padrão: Swing
        _timer.Tick += Timer_Tick;

        InitializeTrayIcon();
        StateChanged += MainWindow_StateChanged;
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

        Hide();
        ShowInTaskbar = false;

        if (_trayIcon != null)
            _trayIcon.Visible = true;
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
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }

    private async void Timer_Tick(object? sender, EventArgs e) => await RunScannerAsync();

    private async Task RunScannerAsync()
    {
        if (_isScanning)
            return;

        _isScanning = true;
        _timer.Stop();
        btAtualizar.IsEnabled = false;
        popupBreakdown.IsOpen = false;

        try
        {
            var result = await _scanner.RunAsync(_currentProfile);
            _lastHistory = result.History;
            _lastRanking = result.Ranking;
            ApplyRankingFilter();
            await DispatchAlertsAsync(result.NewSignals);
            dgHistory.ItemsSource = result.History;
            txtWinRate.Text = $"Win Rate: {result.WinRate:F1}%";
            txtAvgReturn.Text = $"Retorno Médio: {result.AverageReturn:F2}%";
            txtPending.Text = $"Pendentes: {result.History.Count(signal => !signal.Evaluated)}";
            txtEvaluated.Text = $"Avaliados: {result.History.Count(signal => signal.Evaluated)}";
            txtDiagnostics.Text = $"Filtros (motivos de rejeição): {result.Diagnostics.Summary}";
            Title = $"Scanner [{result.MarketRegime}] | Perfil: {_currentProfile.Name} | WinRate: {result.WinRate:F1}% | Avg: {result.AverageReturn:F2}%";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível concluir a atualização do scanner.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btAtualizar.IsEnabled = true;
            _isScanning = false;
            _timer.Start();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isWindowLoaded = true;
        await RunScannerAsync();
    }

    private async void ProfileChanged(object sender, RoutedEventArgs e)
    {
        // Ignora o Checked disparado durante o carregamento inicial do XAML
        // (rbSwing já nasce com IsChecked="True").
        if (!_isWindowLoaded)
            return;

        _currentProfile = ReferenceEquals(sender, rbIntraday) ? ScanProfile.Intraday : ScanProfile.Swing;

        _timer.Interval = _currentProfile.Name == ScanProfile.Intraday.Name
            ? TimeSpan.FromMinutes(3)
            : TimeSpan.FromMinutes(30);

        await RunScannerAsync();
    }

    private async void BtnBacktest_Click(object sender, RoutedEventArgs e)
    {
        var service = new BinanceExchangeService();
        var candles = await service.GetCandlesAsync("BTCUSDT", "1h", 1000);
        var result = new BacktestEngine().Run(candles);
        MessageBox.Show($"Trades: {result.Trades}\n\nWinRate: {result.WinRate:F2}%\n\nLucro: {result.NetProfit:F2}%");
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
        var window = new BacktestWindow(new BinanceExchangeService(), new AssetAnalyzer())
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

    private async void btAtualizar_Click(object sender, RoutedEventArgs e) => await RunScannerAsync();

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
            var result = await _scanner.LookupSymbolAsync(input, _currentProfile);

            if (result == null)
            {
                MessageBox.Show(
                    $"Não foi possível encontrar dados para \"{input}\".\nVerifique o símbolo (ex.: DOGEUSDT).",
                    "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Remove uma entrada antiga do mesmo símbolo (se existir) e insere a nova no topo.
            var updated = _lastRanking
                .Where(a => !string.Equals(a.Symbol, result.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            updated.Insert(0, result);
            _lastRanking = updated;

            // Se o filtro "só favoritos" estiver ativo e a moeda buscada não for favorita,
            // desativa o filtro pra garantir que o resultado apareça — senão o clique em
            // "Buscar" pareceria não ter feito nada.
            if (chkFavoritesOnly.IsChecked == true && !result.IsFavorite)
                chkFavoritesOnly.IsChecked = false;

            ApplyRankingFilter();

            dgRanking.SelectedItem = result;
            dgRanking.ScrollIntoView(result);
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

    private void ApplyRankingFilter()
    {
        dgRanking.ItemsSource = chkFavoritesOnly.IsChecked == true
            ? _lastRanking.Where(a => a.IsFavorite).ToList()
            : _lastRanking;
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

    private void DgRankingRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row || row.Item is not AssetScore asset)
            return;

        // Clicar na mesma linha que já está aberta fecha o popup (comportamento de alternância).
        if (popupBreakdown.IsOpen && ReferenceEquals(popupBreakdown.DataContext, asset))
        {
            popupBreakdown.IsOpen = false;
            return;
        }

        popupBreakdown.DataContext = asset;
        popupBreakdown.PlacementTarget = row;
        popupBreakdown.Placement = PlacementMode.Bottom;
        popupBreakdown.IsOpen = true;
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
}
