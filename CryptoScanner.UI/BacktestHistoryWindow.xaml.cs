using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class BacktestHistoryWindow : Window
{
    private readonly IBacktestRunResultRepository _repository;

    public BacktestHistoryWindow(IBacktestRunResultRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        Loaded += BacktestHistoryWindow_Loaded;
    }

    private async void BacktestHistoryWindow_Loaded(object sender, RoutedEventArgs e) => await LoadResultsAsync();

    private async System.Threading.Tasks.Task LoadResultsAsync()
    {
        try
        {
            await _repository.InitializeAsync();
            var results = await _repository.GetAllAsync();
            dgResults.ItemsSource = results;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o histórico.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        var itemsToExport = dgResults.SelectedItems.Cast<BacktestRunResult>().ToList();

        if (itemsToExport.Count == 0)
            itemsToExport = (dgResults.ItemsSource as System.Collections.Generic.IEnumerable<BacktestRunResult>)?.ToList()
                             ?? new System.Collections.Generic.List<BacktestRunResult>();

        if (itemsToExport.Count == 0)
        {
            MessageBox.Show("Não há resultados pra exportar.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"backtest_historico_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Arquivo de texto (*.txt)|*.txt",
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"Histórico de Testes de Backtest — exportado em {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Total de registros: {itemsToExport.Count}");
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();

        foreach (var r in itemsToExport)
            sb.Append(FormatResultAsText(r));

        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString());
            MessageBox.Show($"Exportado com sucesso:\n{dialog.FileName}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar o arquivo.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatResultAsText(BacktestRunResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Teste: {r.Label}");
        sb.AppendLine($"Salvo em: {r.SavedAt:dd/MM/yy HH:mm}");
        sb.AppendLine($"Perfil: {r.Profile} | Modo de Risco: {r.RiskMode}");
        sb.AppendLine($"Período: {r.StartDate:dd/MM/yy} - {r.EndDate:dd/MM/yy}");
        sb.AppendLine($"Moedas ({r.SymbolCount}): {r.Symbols}");
        sb.AppendLine($"Limiares: Score>={r.MinScore:F0} | RR min={r.MinRiskReward:F1} | RR max={r.MaxRiskReward:F0} | " +
                       $"Dist.Resist.Swing={r.MinResistanceDistanceSwing:F0}% | Dist.Resist.ATR={r.MinResistanceDistanceAtr:F0}% | " +
                       $"Vol.Spike={r.MinVolumeSpike:F2} | Stop min={r.MinStopDistancePercent:F0}% | " +
                       (r.MaxStopDistancePercent.HasValue ? $"Stop máx={r.MaxStopDistancePercent:F0}% | " : "") +
                       $"Caminho A={(r.EnablePullbackBounce ? "sim" : "não")} | Bollinger Scoring={(r.EnableBollingerScoring ? "sim" : "não")} | " +
                       $"Volatility Fase B={(r.EnableVolatilityScoringPhaseB ? "sim" : "não")} | " +
                       $"Timeout override={(r.EvaluationHoursOverride?.ToString() ?? "padrão")}" +
                       (r.DisableTimeout ? " | Timeout=DESATIVADO (só TP/SL)" : "") +
                       (r.Tp1Fraction.HasValue ? $" | Frações TP1/TP2={r.Tp1Fraction:F2}/{r.Tp2Fraction:F2}" : ""));
        sb.AppendLine(new string('-', 70));
        sb.AppendLine($"Operações: {r.TotalTrades} | Win Rate: {r.WinRate:F1}% | Retorno: {r.TotalReturnPercent:F2}% | " +
                       $"Drawdown: {r.MaxDrawdownPercent:F2}% | Profit Factor: {r.ProfitFactor:F2}");
        sb.AppendLine($"RR Médio: {r.AvgRiskRewardAtEntry:F2} | Win Rate Equilíbrio: {r.BreakEvenWinRate:F1}% | Edge: {r.Edge:F1} pontos %");
        if (!string.IsNullOrWhiteSpace(r.Diagnostics))
            sb.AppendLine($"Filtros: {r.Diagnostics}");
        sb.AppendLine(new string('=', 70));
        sb.AppendLine();
        return sb.ToString();
    }

    private void DgResults_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Garante que a linha sob o cursor fica selecionada antes do menu abrir,
        // pra "Copiar" sempre agir na linha certa, mesmo sem clique esquerdo antes.
        var hit = (DependencyObject)e.OriginalSource;
        while (hit != null && hit is not System.Windows.Controls.DataGridRow)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);

        if (hit is System.Windows.Controls.DataGridRow row)
            row.IsSelected = true;
    }

    private void MenuCopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (dgResults.SelectedItem is not BacktestRunResult result)
        {
            MessageBox.Show("Clique com o botão direito em uma linha primeiro.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(FormatResultAsText(result));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível copiar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not BacktestRunResult result)
            return;

        var confirm = MessageBox.Show($"Excluir o resultado \"{result.Label}\" ({result.SavedAt:dd/MM/yy HH:mm})?",
            "CryptoScanner", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            await _repository.DeleteAsync(result.Id);
            await LoadResultsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível excluir.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}