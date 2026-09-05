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
    private readonly Func<Task<decimal>> _getCurrentPrice;
    private bool _saving;

    public bool Saved { get; private set; }

    public SimulateTradeWindow(ISimulatedTradeRepository repository, AssetScore asset, string profileName,
        Func<Task<decimal>> getCurrentPrice)
    {
        InitializeComponent();
        _repository = repository;
        _asset = asset;
        _profileName = profileName;
        _getCurrentPrice = getCurrentPrice;

        txtSymbolHeader.Text = asset.Symbol;
        txtEntryPrice.Text = asset.Close.ToString("0.########");
        txtTakeProfit.Text = asset.Resistance.ToString("0.########");
        txtStopLoss.Text = asset.Support.ToString("0.########");
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_saving)
            return;

        if (!decimal.TryParse(txtTakeProfit.Text, out decimal takeProfit) ||
            !decimal.TryParse(txtStopLoss.Text, out decimal stopLoss))
        {
            MessageBox.Show("Preencha TP e SL com números válidos.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _saving = true;
        IsEnabled = false;
        try
        {
            await _repository.InitializeAsync();
            decimal entryPrice = await _getCurrentPrice();
            txtEntryPrice.Text = entryPrice.ToString("0.########");

            if (entryPrice <= 0 || stopLoss <= 0 || stopLoss >= entryPrice || takeProfit <= entryPrice)
            {
                MessageBox.Show("Trade não aberto: o stop deve ser positivo e menor que a cotação atual, e o TP maior. Atualize a análise ou ajuste os níveis.",
                    "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_asset.TakeProfit1.HasValue &&
                (_asset.TakeProfit1.Value <= entryPrice || _asset.TakeProfit1.Value >= takeProfit ||
                 !_asset.TakeProfit3.HasValue || _asset.TakeProfit3.Value <= takeProfit))
            {
                MessageBox.Show("Trade não aberto: os alvos devem seguir entrada < TP1 < TP2 < TP3. Atualize a análise antes de simular.",
                    "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            await _repository.AddAsync(trade);
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _saving = false;
            IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
