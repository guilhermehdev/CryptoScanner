using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace CryptoScanner.UI;

public partial class SimulateTradeWindow : Window
{
    private readonly ISimulatedTradeRepository _repository;
    private readonly AssetScore _asset;
    private readonly string _profileName;

    public bool Saved { get; private set; }

    public SimulateTradeWindow(ISimulatedTradeRepository repository, AssetScore asset, string profileName)
    {
        InitializeComponent();
        _repository = repository;
        _asset = asset;
        _profileName = profileName;

        txtSymbolHeader.Text = asset.Symbol;
        txtEntryPrice.Text = asset.Close.ToString("0.########");
        txtTakeProfit.Text = asset.Resistance.ToString("0.########");
        txtStopLoss.Text = asset.Support.ToString("0.########");
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(txtEntryPrice.Text, out decimal entryPrice) ||
            !decimal.TryParse(txtTakeProfit.Text, out decimal takeProfit) ||
            !decimal.TryParse(txtStopLoss.Text, out decimal stopLoss))
        {
            MessageBox.Show("Preencha preço de entrada, TP e SL com números válidos.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (takeProfit <= entryPrice || stopLoss >= entryPrice)
        {
            var result = MessageBox.Show(
                "O TP deveria ser maior que a entrada, e o SL menor — os valores atuais parecem invertidos. Salvar mesmo assim?",
                "CryptoScanner", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        var trade = new SimulatedTrade
        {
            Symbol = _asset.Symbol,
            EntryTime = DateTime.UtcNow,
            EntryPrice = entryPrice,
            TakeProfit = takeProfit,
            StopLoss = stopLoss,
            TakeProfit1 = _asset.TakeProfit1,
            TakeProfit3 = _asset.TakeProfit3,
            Note = txtNote.Text.Trim(),
            Profile = _profileName,

            ScoreAtEntry = _asset.Score,
            Rsi = _asset.Rsi,
            Adx = _asset.Adx,
            AtrPercent = _asset.AtrPercent,
            EmaDistanceAtr = _asset.EmaDistanceAtr,
            SwingUsageAtr = _asset.SwingUsageAtr,
            VolumeSpike = _asset.VolumeSpike,
            VolumeImbalance = _asset.VolumeImbalance,
            RelativeStrength = _asset.RelativeStrength,
            RiskRewardAtEntry = _asset.RiskReward,
            TrendScore = _asset.TrendScore,
            StructureScore = _asset.StructureScore,
            VolumeScore = _asset.VolumeScore,
            CandleScore = _asset.CandleScore,
            SetupScore = _asset.SetupScore,
            MomentumScore = _asset.MomentumScore,
            VolatilityScore = _asset.VolatilityScore,
            TrendStrengthScore = _asset.TrendStrengthScore,
            PatternName = _asset.PatternName,
            SmartMoneyLabel = _asset.SmartMoneyLabel,
            BreakoutSource = _asset.BreakoutSource,
            MarketRegime = _asset.MarketRegime,
            IsBullTrap = _asset.IsBullTrap,
            IsBearTrap = _asset.IsBearTrap
        };

        try
        {
            await _repository.InitializeAsync();
            await _repository.AddAsync(trade);
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}