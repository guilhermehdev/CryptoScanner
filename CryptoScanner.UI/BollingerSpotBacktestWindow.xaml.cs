using CryptoScanner.Backtest.Models;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Strategies;
using System.Globalization;
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
            !Enum.TryParse<BollingerSpotTimeframe>(selectedTimeframe.Tag?.ToString(), out var selectedFrame))
        { summary.Text = "Selecione uma variante ativa e um timeframe válido."; return; }
        var settings = new BollingerSpotSettings(selected, selectedFrame);
        if (settings.Variant == BollingerSpotVariant.Disabled) { summary.Text = "Escolha uma variante Bollinger para rodar o teste."; return; }
        if (!TryReadPeriodAndCosts(out var from, out var to, out var costs)) return;

        run.IsEnabled = compare.IsEnabled = false;
        try
        {
            await _applySettings(settings);
            var assets = await ResolveAssetsAsync();
            if (assets.Length == 0) return;
            var candlesBySymbol = await LoadClosedCandlesAsync(assets, settings.Timeframe, from, to);
            var all = new List<BollingerSpotBacktestTrade>();
            var backtester = new BollingerSpotBacktester();
            foreach (var (asset, candles) in candlesBySymbol)
                all.AddRange(backtester.Run(asset, candles, settings, costs).Trades.Where(t => t.EntryTimeUtc >= from.ToUniversalTime()));
            trades.ItemsSource = all.OrderByDescending(t => t.EntryTimeUtc).ToList();
            var closedTrades = all.Where(t => t.Closed).ToList();
            string universe = top30Liquidity.IsChecked == true ? "30 mais líquidos da Binance" : $"{assets.Length} ativo(s) manual(is)";
            summary.Text = $"{settings.DisplayName} · {universe} · {all.Count} entrada(s), {closedTrades.Count} fechada(s) · retorno bruto: {closedTrades.Sum(t => t.GrossReturnPercent ?? 0m):F2}% · líquido: {closedTrades.Sum(t => t.NetReturnPercent ?? 0m):F2}% · taxa {costs.FeePercentPerOrder:F2}% + slippage {costs.SlippagePercentPerOrder:F2}% por ordem. Estratégia experimental.";
            resultTabs.SelectedIndex = 0;
        }
        catch (Exception ex) { summary.Text = $"Não foi possível rodar: {ex.Message}"; }
        finally { run.IsEnabled = compare.IsEnabled = true; }
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPeriodAndCosts(out var from, out var to, out var costs)) return;
        run.IsEnabled = compare.IsEnabled = false;
        try
        {
            var assets = await ResolveAssetsAsync();
            if (assets.Length == 0) return;
            var candlesBySymbol = await LoadClosedCandlesAsync(assets, BollingerSpotTimeframe.Daily, from, to);
            var variants = new[]
            {
                BollingerSpotVariant.Bollinger20x3,
                BollingerSpotVariant.Bollinger20x23,
                BollingerSpotVariant.Bollinger25x3,
                BollingerSpotVariant.Bollinger25x23
            };
            var backtester = new BollingerSpotBacktester();
            var rows = variants.Select(variantToCompare =>
            {
                var settings = new BollingerSpotSettings(variantToCompare, BollingerSpotTimeframe.Daily);
                var result = new BollingerSpotBacktestResult(candlesBySymbol
                    .SelectMany(pair => backtester.Run(pair.Key, pair.Value, settings, costs).Trades
                        .Where(trade => trade.EntryTimeUtc >= from.ToUniversalTime()))
                    .ToArray());
                return result.ToComparison(settings);
            }).OrderByDescending(row => row.NetReturnPercent).ToList();

            comparison.ItemsSource = rows;
            summary.Text = $"Comparação diária: mesmos ativos, período e custos em todas as variantes. Líquido considera taxa {costs.FeePercentPerOrder:F2}% + slippage {costs.SlippagePercentPerOrder:F2}% por ordem; não promove automaticamente nenhuma variante.";
            resultTabs.SelectedIndex = 1;
        }
        catch (Exception ex) { summary.Text = $"Não foi possível comparar: {ex.Message}"; }
        finally { run.IsEnabled = compare.IsEnabled = true; }
    }

    private bool TryReadPeriodAndCosts(out DateTime from, out DateTime to, out BollingerSpotBacktestCosts costs)
    {
        from = start.SelectedDate ?? default;
        to = end.SelectedDate ?? default;
        costs = BollingerSpotBacktestCosts.Default;
        if (from == default || to == default || from >= to)
        {
            summary.Text = "Informe um período válido.";
            return false;
        }
        if (!TryParsePercent(feePercent.Text, out var fee) || !TryParsePercent(slippagePercent.Text, out var slippage) || fee < 0m || slippage < 0m || fee + slippage >= 100m)
        {
            summary.Text = "Taxa e slippage devem ser percentuais não negativos e a soma deve ser menor que 100%.";
            return false;
        }
        costs = new BollingerSpotBacktestCosts(fee, slippage);
        return true;
    }

    private static bool TryParsePercent(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private async Task<string[]> ResolveAssetsAsync()
    {
        if (top30Liquidity.IsChecked == true)
        {
            summary.Text = "Buscando os 30 pares USDT mais líquidos na Binance…";
            return (await _marketData.GetUsdtSymbolsAsync()).Take(30).ToArray();
        }
        return symbols.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(symbol => symbol.ToUpperInvariant().EndsWith("USDT") ? symbol.ToUpperInvariant() : symbol.ToUpperInvariant() + "USDT")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<Dictionary<string, List<Candle>>> LoadClosedCandlesAsync(
        IEnumerable<string> assets,
        BollingerSpotTimeframe selectedTimeframe,
        DateTime from,
        DateTime to)
    {
        var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
        var list = assets.ToArray();
        var interval = selectedTimeframe == BollingerSpotTimeframe.FourHours ? "4h" : "1d";
        var intervalLength = selectedTimeframe == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(4) : TimeSpan.FromDays(1);
        var warmup = selectedTimeframe == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(400) : TimeSpan.FromDays(100);
        DateTime until = to.Date.AddDays(1).ToUniversalTime();

        for (int index = 0; index < list.Length; index++)
        {
            summary.Text = $"Carregando candles fechados: {list[index]} ({index + 1}/{list.Length})…";
            var source = await _marketData.GetHistoricalCandlesAsync(list[index], interval, from.ToUniversalTime() - warmup, until);
            var closed = BollingerSpotCandleAggregator.AggregateUtc(source, selectedTimeframe)
                .Where(candle => candle.OpenTime + intervalLength <= DateTime.UtcNow && candle.OpenTime < until)
                .ToList();
            if (closed.Count >= 100)
                result[list[index]] = closed;
        }
        return result;
    }
}
