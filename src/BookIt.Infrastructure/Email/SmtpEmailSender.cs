using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookIt.Infrastructure.Email;

/// <summary>
/// Uses the built-in BCL <see cref="SmtpClient"/> rather than a third-party mail library. Points
/// at a local dev SMTP catcher by default (e.g. smtp4dev/Papercut via Docker) — see README.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
    {
        var host = configuration["Smtp:Host"] ?? "localhost";
        var port = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 25;
        var from = configuration["Smtp:From"] ?? "noreply@bookit.local";

        using var client = new SmtpClient(host, port);
        using var message = new MailMessage(from, toAddress, subject, body);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException ex)
        {
            // Local dev SMTP catcher may not be running — log and move on instead of crashing
            // the background job, since a missed reminder email isn't fatal.
            logger.LogWarning(ex, "Failed to send email to {ToAddress}. Is a local SMTP catcher running on {Host}:{Port}?", toAddress, host, port);
        }
    }
}
