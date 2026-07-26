using CryptoScanner.Application.Services;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Infrastructure.Sqlite;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly ScannerService _scanner;
    private bool _isScanning;
    private bool _isWindowLoaded;
    private ScanProfile _currentProfile = ScanProfile.Swing;
    private IReadOnlyList<SignalHistory> _lastHistory = Array.Empty<SignalHistory>();
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        var databasePath = GetDatabasePath();

        _scanner = new ScannerService(
            new BinanceExchangeService(),
            new SqliteSignalRepository(databasePath),
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
            dgRanking.ItemsSource = result.Ranking;
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

    private async void btAtualizar_Click(object sender, RoutedEventArgs e) => await RunScannerAsync();

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
