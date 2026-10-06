using CryptoScanner.Core.Models;
using CryptoScanner.Application.Services;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class TechnicalSetupHistoryWindow : Window
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<TechnicalSetupAlert>>> _loadAll;
    private readonly CancellationTokenSource _closed = new();
    private bool _exporting;

    public TechnicalSetupHistoryWindow(
        IReadOnlyList<TechnicalSetupAlert> alerts,
        Func<CancellationToken, Task<IReadOnlyList<TechnicalSetupAlert>>> loadAll)
    {
        InitializeComponent();
        _loadAll = loadAll;
        dgSetups.ItemsSource = alerts;
        dgEvolution.ItemsSource = TechnicalSetupPerformanceAnalyzer.Build(alerts);
        Closed += (_, _) => _closed.Cancel();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar histórico e evolução dos setups",
            Filter = "CSV UTF-8|*.csv",
            FileName = $"setups_tecnicos_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;

        _exporting = true;
        btnExport.IsEnabled = false;
        try
        {
            var alerts = await _loadAll(_closed.Token);
            string evolutionPath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName)!,
                Path.GetFileNameWithoutExtension(dialog.FileName) + "_evolucao.csv");
            await Task.Run(() => ExportAsync(alerts, dialog.FileName, evolutionPath, _closed.Token), _closed.Token);
            MessageBox.Show(
                $"{alerts.Count:N0} observações exportadas.\n\nHistórico: {dialog.FileName}\nEvolução: {evolutionPath}",
                "Setups técnicos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível exportar os setups: {ex.Message}", "Setups técnicos", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _exporting = false;
            btnExport.IsEnabled = true;
        }
    }

    private static async Task ExportAsync(IReadOnlyList<TechnicalSetupAlert> alerts, string historyPath, string evolutionPath, CancellationToken cancellationToken)
    {
        await using (var history = new StreamWriter(historyPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            await history.WriteLineAsync("Id;CandleUtc;EntradaUtc;RegistradoUtc;Ativo;Lado;Setup;Preco;Score;Perfil;Regime;Retorno1hPercent;Retorno6hPercent;Retorno24hPercent;Mfe24hPercent;Mae24hPercent;Situacao");
            foreach (var alert in alerts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await history.WriteLineAsync(string.Join(';',
                    Csv(alert.Id.ToString(CultureInfo.InvariantCulture)), Csv(Utc(alert.CandleOpenUtc)), Csv(Utc(alert.EntryUtc)), Csv(Utc(alert.RecordedUtc)),
                    Csv(alert.Symbol), Csv(alert.DirectionText), Csv(alert.Setup), Decimal(alert.Price), Decimal(alert.Score), Csv(alert.Profile), Csv(alert.MarketRegime),
                    Decimal(alert.ReturnAfter1HourPercent), Decimal(alert.ReturnAfter6HoursPercent), Decimal(alert.ReturnAfter24HoursPercent),
                    Decimal(alert.MaximumFavorable24HoursPercent), Decimal(alert.MaximumAdverse24HoursPercent), Csv(alert.EvaluationStatus)));
            }
        }

        var summaries = TechnicalSetupPerformanceAnalyzer.Build(alerts);
        await using var evolution = new StreamWriter(evolutionPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await evolution.WriteLineAsync("Setup;Lado;Perfil;Regime;Ocorrencias;Avaliados;Evidencia;Positivas24hPercent;Media1hPercent;Media6hPercent;Media24hPercent;ProfitFactor24h;MfeMedio24hPercent;MaeMedio24hPercent");
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await evolution.WriteLineAsync(string.Join(';',
                Csv(summary.Setup), Csv(summary.Direction == CryptoScanner.Core.Configuration.TradeDirection.Short ? "Venda" : "Compra"), Csv(summary.Profile), Csv(summary.MarketRegime),
                Csv(summary.Occurrences.ToString(CultureInfo.InvariantCulture)), Csv(summary.Evaluated.ToString(CultureInfo.InvariantCulture)), Csv(summary.EvidenceStatus),
                Decimal(summary.Positive24HoursPercent), Decimal(summary.AverageReturn1HourPercent), Decimal(summary.AverageReturn6HoursPercent),
                Decimal(summary.AverageReturn24HoursPercent), Decimal(summary.GrossProfitFactor24Hours), Decimal(summary.AverageMaximumFavorable24HoursPercent),
                Decimal(summary.AverageMaximumAdverse24HoursPercent)));
        }
    }

    private static string Utc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Decimal(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";
    private static string Decimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
