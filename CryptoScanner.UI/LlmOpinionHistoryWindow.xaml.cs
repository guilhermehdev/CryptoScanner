using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class LlmOpinionHistoryWindow : Window
{
    private readonly ILlmOpinionRepository _repository;
    private readonly ObservableCollection<LlmOpinionRecord> _rows = [];
    private List<LlmOpinionRecord> _all = [];

    public LlmOpinionHistoryWindow(ILlmOpinionRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        dgOpinions.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            await _repository.InitializeAsync();
            _all = (await _repository.GetRecentAsync(1000)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar o histórico da LLM.\n{ex.Message}", "Histórico da LLM", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        var symbol = txtSymbolFilter.Text.Trim();
        var decision = (cmbDecisionFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var filtered = _all.Where(row =>
            (symbol.Length == 0 || row.Symbol.Contains(symbol, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(decision) || decision == "Todas" || row.Decision.Equals(decision, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _rows.Clear();
        foreach (var row in filtered)
            _rows.Add(row);
        txtSummary.Text = $"{filtered.Count} opinião(ões) exibida(s) de {_all.Count} registrada(s).";
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("Não há opiniões para exportar com os filtros atuais.", "Histórico da LLM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar histórico da LLM",
            Filter = "CSV UTF-8|*.csv",
            FileName = $"llm_opinioes_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var csv = new StringBuilder();
        csv.AppendLine("Data;Ativo;Perfil;Scanner;Decisao;Direcao;Confianca;ResultadoPercent;Saida;Preco;Entrada;Stop;TP1;TP2;Tendencia;Motivos;Riscos;Imagem");
        foreach (var row in _rows)
        {
            csv.AppendLine(string.Join(';',
                Csv(row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")), Csv(row.Symbol), Csv(row.Profile), Csv(row.ScannerSignal),
                Csv(row.Decision), Csv(row.Direction), row.Confidence, Csv(row.OutcomePercent?.ToString("0.##") ?? ""), Csv(row.OutcomeReason), Csv(row.AnalysisPrice.ToString("0.########")),
                Csv(row.Entry?.ToString("0.########") ?? ""), Csv(row.Stop?.ToString("0.########") ?? ""),
                Csv(row.Tp1?.ToString("0.########") ?? ""), Csv(row.Tp2?.ToString("0.########") ?? ""), Csv(row.Trend),
                Csv(row.Reasons), Csv(row.Risks), Csv(row.ImagePath)));
        }

        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show("Histórico exportado com sucesso.", "Histórico da LLM", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
