using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using System;
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
