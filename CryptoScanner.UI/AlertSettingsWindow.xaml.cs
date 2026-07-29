using CryptoScanner.Application.Services;
using CryptoScanner.Backtest.Services;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;
using CryptoScanner.Exchange.Services;
using CryptoScanner.Infrastructure.Sqlite;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace CryptoScanner.UI;

public partial class AlertSettingsWindow : Window
{
    private readonly IAlertSettingsRepository _repository;

    public AlertSettingsWindow(IAlertSettingsRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        Loaded += AlertSettingsWindow_Loaded;
    }

    private async void AlertSettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _repository.InitializeAsync();
        var settings = await _repository.LoadAsync();

        chkDesktop.IsChecked = settings.DesktopEnabled;

        chkTelegram.IsChecked = settings.TelegramEnabled;
        txtTelegramToken.Text = settings.TelegramBotToken;
        txtTelegramChatId.Text = settings.TelegramChatId;

        chkDiscord.IsChecked = settings.DiscordEnabled;
        txtDiscordWebhook.Text = settings.DiscordWebhookUrl;

        chkEmail.IsChecked = settings.EmailEnabled;
        txtEmailHost.Text = string.IsNullOrWhiteSpace(settings.EmailSmtpHost) ? "smtp.gmail.com" : settings.EmailSmtpHost;
        txtEmailPort.Text = settings.EmailSmtpPort.ToString();
        txtEmailUsername.Text = settings.EmailUsername;
        txtEmailPassword.Text = settings.EmailPassword;
        txtEmailFrom.Text = settings.EmailFrom;
        txtEmailTo.Text = settings.EmailTo;
        chkEmailSsl.IsChecked = settings.EmailUseSsl;
    }

    private AlertSettings BuildSettingsFromFields()
    {
        int.TryParse(txtEmailPort.Text, out int smtpPort);

        return new AlertSettings
        {
            DesktopEnabled = chkDesktop.IsChecked == true,
            TelegramEnabled = chkTelegram.IsChecked == true,
            TelegramBotToken = txtTelegramToken.Text.Trim(),
            TelegramChatId = txtTelegramChatId.Text.Trim(),
            DiscordEnabled = chkDiscord.IsChecked == true,
            DiscordWebhookUrl = txtDiscordWebhook.Text.Trim(),
            EmailEnabled = chkEmail.IsChecked == true,
            EmailSmtpHost = txtEmailHost.Text.Trim(),
            EmailSmtpPort = smtpPort > 0 ? smtpPort : 587,
            EmailUsername = txtEmailUsername.Text.Trim(),
            EmailPassword = txtEmailPassword.Text,
            EmailFrom = txtEmailFrom.Text.Trim(),
            EmailTo = txtEmailTo.Text.Trim(),
            EmailUseSsl = chkEmailSsl.IsChecked == true
        };
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _repository.SaveAsync(BuildSettingsFromFields());
            MessageBox.Show("Configurações de alerta salvas.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível salvar.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnTestTelegram_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtTelegramToken.Text) || string.IsNullOrWhiteSpace(txtTelegramChatId.Text))
        {
            MessageBox.Show("Preencha o Bot Token e o Chat ID antes de testar.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await TestChannelAsync(new TelegramAlertChannel(txtTelegramToken.Text.Trim(), txtTelegramChatId.Text.Trim()));
    }

    private async void BtnTestDiscord_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtDiscordWebhook.Text))
        {
            MessageBox.Show("Preencha a URL do Webhook antes de testar.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await TestChannelAsync(new DiscordAlertChannel(txtDiscordWebhook.Text.Trim()));
    }

    private async void BtnTestEmail_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtEmailHost.Text) || string.IsNullOrWhiteSpace(txtEmailTo.Text))
        {
            MessageBox.Show("Preencha ao menos o servidor SMTP e o destinatário antes de testar.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(txtEmailPort.Text, out int port);

        await TestChannelAsync(new EmailAlertChannel(
            txtEmailHost.Text.Trim(),
            port > 0 ? port : 587,
            txtEmailUsername.Text.Trim(),
            txtEmailPassword.Text,
            txtEmailFrom.Text.Trim(),
            txtEmailTo.Text.Trim(),
            chkEmailSsl.IsChecked == true));
    }

    private async System.Threading.Tasks.Task TestChannelAsync(IAlertChannel channel)
    {
        try
        {
            await channel.SendAsync("Teste de Alerta — CryptoScanner", "Este é um alerta de teste. Se você recebeu isso, o canal está configurado corretamente.");
            MessageBox.Show($"Alerta de teste enviado com sucesso via {channel.Name}.", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao enviar teste via {channel.Name}.\n{ex.Message}", "CryptoScanner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
