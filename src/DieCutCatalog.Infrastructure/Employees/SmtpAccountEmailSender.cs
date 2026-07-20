using System.Net;
using System.Net.Mail;
using System.Text;
using DieCutCatalog.Application.Employees;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Employees;

public sealed class SmtpAccountEmailSender(IOptions<SmtpOptions> options)
    : IAccountEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendTemporaryPasswordAsync(
        string recipientEmail,
        string employeeName,
        string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new EmailDeliveryUnavailableException(
                "SMTP is not configured. The account was not created.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = "Доступ к каталогу вырубных ножей",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
            Body = $"""
                Здравствуйте, {employeeName}!

                Для вас создана учётная запись в системе DieCut Catalog.

                Логин: {recipientEmail}
                Временный пароль: {temporaryPassword}

                При первом входе система потребует задать новый пароль.
                Если вы не ожидали это письмо, сообщите администратору.
                """
        };
        message.To.Add(recipientEmail);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
        {
            throw new EmailDeliveryUnavailableException(
                "The invitation email could not be sent. The account was not created.",
                exception);
        }
    }
}
