using CryptoScanner.Backtest.Models;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Strategies;
using System.Windows;
using System.Windows.Controls;

namespace CryptoScanner.UI;

public partial class BollingerSpotBacktestWindow : Window
{
    private readonly IMarketDataService _marketData;
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly Func<BollingerSpotSettings, Task> _applySettings;

    public BollingerSpotBacktestWindow(
        IMarketDataService marketData,
        IAppSettingsRepository settingsRepository,
        Func<BollingerSpotSettings, Task> applySettings)
    {
        InitializeComponent();
        _marketData = marketData;
        _settingsRepository = settingsRepository;
        _applySettings = applySettings;
        start.SelectedDate = new DateTime(2020, 1, 1);
        end.SelectedDate = new DateTime(2026, 12, 30);
        Loaded += BollingerSpotBacktestWindow_Loaded;
    }

    private async void BollingerSpotBacktestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _settingsRepository.InitializeAsync();
            var storedVariant = await _settingsRepository.GetAsync(MainWindow.BollingerSpotVariantSettingKey);
            var storedTimeframe = await _settingsRepository.GetAsync(MainWindow.BollingerSpotTimeframeSettingKey);
            SelectByTag(variant, Enum.TryParse<BollingerSpotVariant>(storedVariant, out var savedVariant) ? savedVariant : BollingerSpotSettings.Default.Variant);
            SelectByTag(timeframe, Enum.TryParse<BollingerSpotTimeframe>(storedTimeframe, out var savedFrame) ? savedFrame : BollingerSpotSettings.Default.Timeframe);
        }
        catch
        {
            // A tela permanece no padrão Diário + 20/3 se a preferência ainda não existir.
        }
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, object value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (variant.SelectedItem is not ComboBoxItem selectedVariant || timeframe.SelectedItem is not ComboBoxItem selectedTimeframe ||
            !Enum.TryParse<BollingerSpotVariant>(selectedVariant.Tag?.ToString(), out var selected) ||
            !Enum.TryParse<BollingerSpotTimeframe>(selectedTimeframe.Tag?.ToString(), out var selectedFrame) ||
            start.SelectedDate is not DateTime from || end.SelectedDate is not DateTime to || from >= to)
        { summary.Text = "Selecione uma variante ativa e um período válido."; return; }
        var settings = new BollingerSpotSettings(selected, selectedFrame);
        if (settings.Variant == BollingerSpotVariant.Disabled) { summary.Text = "Escolha uma variante Bollinger para rodar o teste."; return; }
        run.IsEnabled = false; summary.Text = "Carregando candles fechados…";
        try
        {
            await _applySettings(settings);
            var assets = top30Liquidity.IsChecked == true
                ? (await _marketData.GetUsdtSymbolsAsync()).Take(30).ToArray()
                : symbols.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.ToUpperInvariant().EndsWith("USDT") ? s.ToUpperInvariant() : s.ToUpperInvariant() + "USDT")
                    .Distinct()
                    .ToArray();
            if (assets.Length == 0) { summary.Text = "A Binance não retornou ativos líquidos disponíveis."; return; }
            var all = new List<BollingerSpotBacktestTrade>();
            var backtester = new BollingerSpotBacktester();
            var warmup = selectedFrame == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(4 * 100) : TimeSpan.FromDays(100);
            DateTime until = to.Date.AddDays(1).ToUniversalTime();
            foreach (var asset in assets)
            {
                var candles = await _marketData.GetHistoricalCandlesAsync(asset, settings.Interval, from.ToUniversalTime() - warmup, until);
                // O provedor já entrega 4h/1d em UTC. Reagrupar por bucket UTC mantém
                // a regra OHLC explícita e é uma operação identidade para candles já
                // alinhados, sem recorrer a candles de 1 minuto.
                var closed = BollingerSpotCandleAggregator.AggregateUtc(candles, selectedFrame)
                    .Where(c => c.OpenTime + (selectedFrame == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(4) : TimeSpan.FromDays(1)) <= DateTime.UtcNow && c.OpenTime < until)
                    .ToList();
                if (closed.Count < 100) continue;
                all.AddRange(backtester.Run(asset, closed, settings).Trades.Where(t => t.EntryTimeUtc >= from.ToUniversalTime()));
            }
            trades.ItemsSource = all.OrderByDescending(t => t.EntryTimeUtc).ToList();
            var closedTrades = all.Where(t => t.Closed).ToList();
            string universe = top30Liquidity.IsChecked == true ? "30 mais líquidos da Binance" : $"{assets.Length} ativo(s) manual(is)";
            summary.Text = $"{settings.DisplayName} · {universe} · {all.Count} entrada(s), {closedTrades.Count} fechada(s) · retorno somado: {closedTrades.Sum(t => t.ReturnPercent ?? 0m):F2}% · candles fechados em UTC. Estratégia experimental, sem comprovação de rentabilidade.";
        }
        catch (Exception ex) { summary.Text = $"Não foi possível rodar: {ex.Message}"; }
        finally { run.IsEnabled = true; }
    }
}
