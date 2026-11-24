using System.Net;
using CodeReviewAgents.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CodeReviewAgents.Services;

public sealed class SmtpEmailService
{
    private readonly SmtpSettings _settings;

    public SmtpEmailService(SmtpSettings settings)
    {
        _settings = settings;
    }

    public async Task<(bool Sent, string StatusMessage)> SendPrReviewEmailAsync(
        string subject,
        string markdownBody,
        string? prCommentUrl,
        CancellationToken cancellationToken = default)
    {
        if (_settings.DryRun)
        {
            return (
                Sent: false,
                StatusMessage: $"[DRY-RUN] E-posta gönderimi simüle edildi -> Alıcı: {(_settings.ToEmail is { Length: > 0 } ? _settings.ToEmail : "(tanımsız)")}, Konu: {subject}"
            );
        }

        if (string.IsNullOrWhiteSpace(_settings.Host)
            || string.IsNullOrWhiteSpace(_settings.FromEmail)
            || string.IsNullOrWhiteSpace(_settings.ToEmail))
        {
            throw new InvalidOperationException("SMTP ayarları (Host, FromEmail, ToEmail) eksik. Gerçek mail göndermek için doldurun veya DryRun=true yapın.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));

        foreach (var recipient in _settings.ToEmail.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = subject;

        var prLinkText = string.IsNullOrWhiteSpace(prCommentUrl)
            ? string.Empty
            : $"\n\nGitHub PR Yorumu: {prCommentUrl}";

        var prLinkHtml = string.IsNullOrWhiteSpace(prCommentUrl)
            ? string.Empty
            : $"<p><strong>GitHub PR Yorumu:</strong> <a href=\"{WebUtility.HtmlEncode(prCommentUrl)}\">{WebUtility.HtmlEncode(prCommentUrl)}</a></p>";

        var bodyBuilder = new BodyBuilder
        {
            TextBody = $"{markdownBody}{prLinkText}",
            HtmlBody = $"""
                <html>
                  <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.5; color: #1f2937;">
                    <h2 style="color: #111827;">AI Code Review Raporu (Lead Agent Onaylı)</h2>
                    {prLinkHtml}
                    <pre style="white-space: pre-wrap; background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px;">{WebUtility.HtmlEncode(markdownBody)}</pre>
                  </body>
                </html>
                """
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var smtp = new SmtpClient();
        var secureSocketOption = _settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await smtp.ConnectAsync(_settings.Host, _settings.Port, secureSocketOption, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            await smtp.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
        }

        await smtp.SendAsync(new SmtpSendRequest(message), cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);

        return (
            Sent: true,
            StatusMessage: $"PR yorumu e-posta ile başarıyla gönderildi -> {_settings.ToEmail}"
        );
    }
}
