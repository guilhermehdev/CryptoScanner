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
    private const string FrozenUniverseSettingKey = "BollingerSpotFrozenUniverse";
    private string[] _frozenUniverse = Array.Empty<string>();

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
            var settings = new BollingerSpotSettings(
                Enum.TryParse<BollingerSpotVariant>(storedVariant, out var savedVariant) ? savedVariant : BollingerSpotSettings.Default.Variant,
                Enum.TryParse<BollingerSpotTimeframe>(storedTimeframe, out var savedFrame) ? savedFrame : BollingerSpotSettings.Default.Timeframe);
            SelectByTag(profile, BollingerSpotExperimentProfile.FromSettings(settings).Key);
            ApplyProfileTimeframe();

            var frozen = await _settingsRepository.GetAsync(FrozenUniverseSettingKey);
            _frozenUniverse = ParseSymbols(frozen);
            if (_frozenUniverse.Length > 0)
            {
                symbols.Text = string.Join(',', _frozenUniverse);
                top30Liquidity.IsChecked = false;
                summary.Text = $"Universo congelado carregado: {_frozenUniverse.Length} ativos. Desmarcado = usa esta mesma lista; marque Top 30 atual para consultar uma lista nova.";
            }
        }
        catch
        {
            // A tela permanece no perfil Base (25/3 Diário) se a preferência não existir.
        }
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox comboBox, object value)
    {
        // O SelectionChanged do perfil pode disparar durante InitializeComponent, antes
        // do ComboBox de timeframe ter sido criado pelo XAML.
        if (comboBox is null || value is null)
            return;

        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
    }

    private BollingerSpotExperimentProfile SelectedProfile =>
        profile.SelectedItem is ComboBoxItem item
            ? BollingerSpotExperimentProfile.FromKey(item.Tag?.ToString())
            : BollingerSpotExperimentProfile.All[0];

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyProfileTimeframe();

    private void ApplyProfileTimeframe() => SelectByTag(timeframe, SelectedProfile.Settings.Timeframe);

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var selectedProfile = SelectedProfile;
        var settings = selectedProfile.Settings;
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
            string universe = DescribeUniverse(assets);
            summary.Text = $"{selectedProfile.DisplayName} · {universe} · {all.Count} entrada(s), {closedTrades.Count} fechada(s) · retorno bruto: {closedTrades.Sum(t => t.GrossReturnPercent ?? 0m):F2}% · líquido: {closedTrades.Sum(t => t.NetReturnPercent ?? 0m):F2}% · taxa {costs.FeePercentPerOrder:F2}% + slippage {costs.SlippagePercentPerOrder:F2}% por ordem. Estratégia experimental.";
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
            var profiles = BollingerSpotExperimentProfile.All;
            var candlesByTimeframe = new Dictionary<BollingerSpotTimeframe, Dictionary<string, List<Candle>>>();
            foreach (var timeframeToLoad in profiles.Select(experiment => experiment.Settings.Timeframe).Distinct())
                candlesByTimeframe[timeframeToLoad] = await LoadClosedCandlesAsync(assets, timeframeToLoad, from, to);
            var backtester = new BollingerSpotBacktester();
            var rows = profiles.Select(profileToCompare =>
            {
                var settings = profileToCompare.Settings;
                var result = new BollingerSpotBacktestResult(candlesByTimeframe[settings.Timeframe]
                    .SelectMany(pair => backtester.Run(pair.Key, pair.Value, settings, costs).Trades
                        .Where(trade => trade.EntryTimeUtc >= from.ToUniversalTime()))
                    .ToArray());
                return result.ToComparison(settings) with { ProfileName = profileToCompare.DisplayName };
            }).OrderByDescending(row => row.NetReturnPercent).ToList();

            comparison.ItemsSource = rows;
            summary.Text = $"Comparação Base + A/B · {DescribeUniverse(assets)}: mesmos ativos, período e custos em todos os perfis. Líquido considera taxa {costs.FeePercentPerOrder:F2}% + slippage {costs.SlippagePercentPerOrder:F2}% por ordem; não promove automaticamente nenhuma variante.";
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

    private async void FreezeTop30_Click(object sender, RoutedEventArgs e)
    {
        freezeTop30.IsEnabled = false;
        try
        {
            summary.Text = "Buscando e congelando os 30 pares USDT mais líquidos na Binance…";
            _frozenUniverse = (await _marketData.GetUsdtSymbolsAsync()).Take(30).ToArray();
            if (_frozenUniverse.Length == 0)
            {
                summary.Text = "A Binance não retornou ativos para congelar.";
                return;
            }

            await _settingsRepository.InitializeAsync();
            await _settingsRepository.SetAsync(FrozenUniverseSettingKey, string.Join(',', _frozenUniverse));
            symbols.Text = string.Join(',', _frozenUniverse);
            top30Liquidity.IsChecked = false;
            summary.Text = $"Universo congelado: {_frozenUniverse.Length} ativos salvos. Os próximos Backtests usarão esta lista enquanto Top 30 atual estiver desmarcado.";
        }
        catch (Exception ex)
        {
            summary.Text = $"Não foi possível congelar o universo: {ex.Message}";
        }
        finally
        {
            freezeTop30.IsEnabled = true;
        }
    }

    private static string[] ParseSymbols(string? text) => (text ?? "")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(symbol => symbol.ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private string DescribeUniverse(IReadOnlyCollection<string> assets)
    {
        bool usesFrozen = top30Liquidity.IsChecked != true && _frozenUniverse.Length > 0 &&
            assets.Count == _frozenUniverse.Length && assets.All(symbol => _frozenUniverse.Contains(symbol, StringComparer.OrdinalIgnoreCase));
        return usesFrozen ? $"universo congelado ({assets.Count} ativos)" :
            top30Liquidity.IsChecked == true ? "Top 30 atual da Binance" : $"{assets.Count} ativo(s) manual(is)";
    }

    private async Task<Dictionary<string, List<Candle>>> LoadClosedCandlesAsync(
        IEnumerable<string> assets,
        BollingerSpotTimeframe selectedTimeframe,
        DateTime from,
        DateTime to)
    {
        var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
        var list = assets.ToArray();
        var settings = new BollingerSpotSettings(BollingerSpotVariant.Bollinger25x3, selectedTimeframe);
        var interval = settings.Interval;
        var intervalLength = settings.CandleDuration;
        var warmup = TimeSpan.FromTicks(intervalLength.Ticks * 100);
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
