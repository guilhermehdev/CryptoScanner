using System.Net;
using System.Net.Mail;

namespace CryptoScanner.Application.Services;

public sealed class EmailAlertChannel : IAlertChannel
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _username;
    private readonly string _password;
    private readonly string _from;
    private readonly string _to;
    private readonly bool _useSsl;

    public string Name => "Email";

    public EmailAlertChannel(string smtpHost, int smtpPort, string username, string password, string from, string to, bool useSsl)
    {
        _smtpHost = smtpHost;
        _smtpPort = smtpPort;
        _username = username;
        _password = password;
        _from = from;
        _to = to;
        _useSsl = useSsl;
    }

    public async Task SendAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_smtpHost, _smtpPort)
        {
            Credentials = new NetworkCredential(_username, _password),
            EnableSsl = _useSsl
        };

        using var mail = new MailMessage(_from, _to, title, message);
        await client.SendMailAsync(mail, cancellationToken);
    }
}