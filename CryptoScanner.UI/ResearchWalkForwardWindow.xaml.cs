using CryptoScanner.Application.Services;
using CryptoScanner.Core.Models;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class ResearchWalkForwardWindow : Window
{
    public ResearchWalkForwardWindow()
    {
        InitializeComponent();
    }

    private void BtnSelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Resumos de pesquisa (*_pesquisa_resumo.csv)|*_pesquisa_resumo.csv|Arquivos CSV (*.csv)|*.csv",
            Multiselect = true,
            Title = "Selecione os resumos de períodos independentes"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var periods = dialog.FileNames
                .Select(path => (Period: System.IO.Path.GetFileNameWithoutExtension(path), Summaries: (IEnumerable<ResearchCandidateSummary>)ReadSummaries(path)))
                .ToList();
            var comparisons = CandidateResearchWalkForwardComparer.Compare(periods);
            dgComparison.ItemsSource = comparisons.Select(ToRow).ToList();
            txtFiles.Text = $"{periods.Count} resumo(s) carregado(s); {comparisons.Count} fatores comparáveis.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível ler os resumos.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<ResearchCandidateSummary> ReadSummaries(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2 || !lines[0].StartsWith("Dimension;Bucket;Count;", StringComparison.Ordinal))
            throw new InvalidDataException($"{System.IO.Path.GetFileName(path)} não é um resumo de pesquisa válido.");

        return lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, index) => ParseRow(line, path, index + 2))
            .ToList();
    }

    private static ResearchCandidateSummary ParseRow(string line, string path, int rowNumber)
    {
        var values = line.Split(';');
        if (values.Length < 9)
            throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}: linha {rowNumber} está incompleta.");

        return new ResearchCandidateSummary
        {
            Dimension = values[0],
            Bucket = values[1],
            Count = ParseInt(values[2], path, rowNumber),
            Positive24HoursPercent = ParseDecimal(values[3], path, rowNumber),
            AverageReturn6HoursPercent = ParseDecimal(values[4], path, rowNumber),
            AverageReturn24HoursPercent = ParseDecimal(values[5], path, rowNumber),
            GrossProfitFactor24Hours = ParseDecimal(values[6], path, rowNumber),
            AverageMaximumFavorable24HoursPercent = ParseDecimal(values[7], path, rowNumber),
            AverageMaximumAdverse24HoursPercent = ParseDecimal(values[8], path, rowNumber)
        };
    }

    private static int ParseInt(string value, string path, int rowNumber) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}: número inválido na linha {rowNumber}.");

    private static decimal ParseDecimal(string value, string path, int rowNumber) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"{System.IO.Path.GetFileName(path)}: decimal inválido na linha {rowNumber}.");

    private static ComparisonRow ToRow(ResearchWalkForwardComparison comparison) => new()
    {
        Dimension = comparison.Dimension,
        Bucket = comparison.Bucket,
        PositiveLabel = $"{comparison.PositivePeriods}/{comparison.LoadedPeriods}",
        TotalCandidates = comparison.TotalCandidates,
        WeightedAverageReturn24HoursPercent = comparison.WeightedAverageReturn24HoursPercent,
        WorstReturn24HoursPercent = comparison.WorstReturn24HoursPercent,
        LowestProfitFactor24Hours = comparison.LowestProfitFactor24Hours,
        Status = comparison.IsConsistentlyPositive ? "Consistente" : "Não confirmado",
        PeriodSummary = string.Join("  |  ", comparison.PeriodResults.Select(result =>
            $"{result.Period}: {result.AverageReturn24HoursPercent:F3}% / PF {result.GrossProfitFactor24Hours:F2} / N {result.Count}"))
    };

    private sealed class ComparisonRow
    {
        public required string Dimension { get; init; }
        public required string Bucket { get; init; }
        public required string PositiveLabel { get; init; }
        public required int TotalCandidates { get; init; }
        public required decimal WeightedAverageReturn24HoursPercent { get; init; }
        public required decimal WorstReturn24HoursPercent { get; init; }
        public required decimal LowestProfitFactor24Hours { get; init; }
        public required string Status { get; init; }
        public required string PeriodSummary { get; init; }
    }
}
