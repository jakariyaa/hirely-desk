using System.Net;
using System.Text;
using System.Text.Json;
using CvPlatform.Core.Support;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CvPlatform.Infrastructure.Support;

public sealed class SupportTicketProcessor(
    IOptions<GoogleOptions> options,
    GoogleCredentialFactory credentials,
    ILogger<SupportTicketProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly GoogleOptions _options = options.Value;

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(_options.DriveProcessedFolderId))
            return;

        var credential = credentials.Create();
        var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });
        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName,
        });

        var listRequest = drive.Files.List();
        listRequest.Q = $"'{_options.DriveFolderId}' in parents and trashed = false and mimeType = 'application/json'";
        listRequest.Fields = "files(id, name)";
        listRequest.OrderBy = "createdTime";
        var pending = (await listRequest.ExecuteAsync(ct)).Files;
        if (pending is null || pending.Count == 0)
            return;

        foreach (var file in pending)
        {
            try
            {
                var get = drive.Files.Get(file.Id);
                using var buffer = new MemoryStream();
                await get.DownloadAsync(buffer, ct);
                var ticket = JsonSerializer.Deserialize<SupportTicket>(buffer.ToArray(), JsonOptions);
                if (ticket is null)
                    continue;

                var subject = $"[Support] [{ticket.Priority}] {Truncate(ticket.Summary, 60)}";
                await SendAsync(gmail, subject, BuildHtml(ticket), ct, ticket);

                var update = drive.Files.Update(new Google.Apis.Drive.v3.Data.File(), file.Id);
                update.AddParents = _options.DriveProcessedFolderId;
                update.RemoveParents = _options.DriveFolderId;
                update.Fields = "id, parents";
                await update.ExecuteAsync(ct);
                logger.LogInformation("Processed support ticket {File}", file.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process support ticket file {Id}", file.Id);
            }
        }
    }

    private async Task SendAsync(GmailService gmail, string subject, string html,
        CancellationToken ct, SupportTicket ticket)
    {
        var admins = ticket.AdminEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
        if (admins.Count == 0)
        {
            logger.LogWarning("Support ticket has no admin recipients; skipping email");
            return;
        }

        var message = new MimeMessage();
        foreach (var admin in admins)
            message.To.Add(MailboxAddress.Parse(admin));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        using var stream = new MemoryStream();
        await message.WriteToAsync(stream, ct);
        var request = gmail.Users.Messages.Send(
            new Message { Raw = Base64UrlEncode(stream.ToArray()) }, "me");
        await request.ExecuteAsync(ct);
        logger.LogInformation("Support notification sent to {Count} admin(s)", admins.Count);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static string BuildHtml(SupportTicket ticket)
    {
        var rows = string.Join("", new (string Label, string? Value)[]
        {
            ("Reported by", ticket.ReportedBy),
            ("Position", ticket.Position),
            ("Link", ticket.Link),
            ("Priority", ticket.Priority),
            ("Summary", ticket.Summary),
            ("Admins", string.Join(", ", ticket.AdminEmails)),
        }.Select(row =>
            $"<tr><td style=\"padding:6px 12px;font-weight:bold;vertical-align:top\">{WebUtility.HtmlEncode(row.Label)}</td><td style=\"padding:6px 12px\">{WebUtility.HtmlEncode(row.Value ?? "")}</td></tr>"));
        return $"""
            <html><body style="font-family:Segoe UI,Arial,sans-serif;background:#f6f8fa;padding:24px">
            <div style="max-width:640px;margin:auto;background:#fff;border-radius:8px;padding:24px;border:1px solid #e1e4e8">
            <h2 style="margin-top:0;color:#24292e">New support ticket</h2>
            <table style="border-collapse:collapse;width:100%">{rows}</table>
            </div></body></html>
            """;
    }
}
