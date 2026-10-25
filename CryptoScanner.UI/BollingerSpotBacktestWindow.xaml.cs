using CryptoScanner.Backtest.Models;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using System.Windows;
using System.Windows.Controls;

namespace CryptoScanner.UI;

public partial class BollingerSpotBacktestWindow : Window
{
    private readonly IMarketDataService _marketData;
    public BollingerSpotBacktestWindow(IMarketDataService marketData)
    {
        InitializeComponent(); _marketData = marketData;
        start.SelectedDate = DateTime.Today.AddYears(-1); end.SelectedDate = DateTime.Today;
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
        var assets = symbols.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToUpperInvariant().EndsWith("USDT") ? s.ToUpperInvariant() : s.ToUpperInvariant() + "USDT").Distinct().ToArray();
        if (assets.Length == 0) { summary.Text = "Informe ao menos um ativo."; return; }
        run.IsEnabled = false; summary.Text = "Carregando candles fechados…";
        try
        {
            var all = new List<BollingerSpotBacktestTrade>();
            var backtester = new BollingerSpotBacktester();
            var warmup = selectedFrame == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(4 * 100) : TimeSpan.FromDays(100);
            DateTime until = to.Date.AddDays(1).ToUniversalTime();
            foreach (var asset in assets)
            {
                var candles = await _marketData.GetHistoricalCandlesAsync(asset, settings.Interval, from.ToUniversalTime() - warmup, until);
                var closed = candles.Where(c => c.OpenTime + (selectedFrame == BollingerSpotTimeframe.FourHours ? TimeSpan.FromHours(4) : TimeSpan.FromDays(1)) <= DateTime.UtcNow && c.OpenTime < until).ToList();
                if (closed.Count < 100) continue;
                all.AddRange(backtester.Run(asset, closed, settings).Trades.Where(t => t.EntryTimeUtc >= from.ToUniversalTime()));
            }
            trades.ItemsSource = all.OrderByDescending(t => t.EntryTimeUtc).ToList();
            var closedTrades = all.Where(t => t.Closed).ToList();
            summary.Text = $"{settings.DisplayName} · {assets.Length} ativo(s) · {all.Count} entrada(s), {closedTrades.Count} fechada(s) · retorno somado: {closedTrades.Sum(t => t.ReturnPercent ?? 0m):F2}% · candles fechados em UTC. Estratégia experimental, sem comprovação de rentabilidade.";
        }
        catch (Exception ex) { summary.Text = $"Não foi possível rodar: {ex.Message}"; }
        finally { run.IsEnabled = true; }
    }
}
