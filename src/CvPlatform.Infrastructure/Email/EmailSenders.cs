using CvPlatform.Core.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CvPlatform.Infrastructure.Email;

public sealed class GmailEmailSender(
    IOptions<GmailOptions> options,
    ILogger<GmailEmailSender> logger) : IAppEmailSender
{
    private readonly GmailOptions _options = options.Value;

    public async Task SendConfirmationLinkAsync(string email, string confirmationLink, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.Address));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = "Confirm your email";
        message.Body = new TextPart("html")
        {
            Text = $"""<p>Please confirm your account by <a href="{confirmationLink}">clicking here</a>.</p>""",
        };

        using var client = new SmtpClient();
        await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(_options.Address, _options.AppPassword, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Confirmation email sent to {Email}", email);
    }
}

public sealed class NoOpEmailSender(ILogger<NoOpEmailSender> logger) : IAppEmailSender
{
    public Task SendConfirmationLinkAsync(string email, string confirmationLink, CancellationToken ct = default)
    {
        logger.LogInformation("Gmail is not configured; skipping confirmation email to {Email}", email);
        return Task.CompletedTask;
    }
}
