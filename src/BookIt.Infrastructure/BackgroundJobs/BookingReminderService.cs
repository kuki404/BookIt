using BookIt.Domain.Enums;
using BookIt.Infrastructure.Email;
using BookIt.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookIt.Infrastructure.BackgroundJobs;

/// <summary>
/// Recurring maintenance job built on the framework's own <see cref="BackgroundService"/> +
/// <see cref="PeriodicTimer"/> (Microsoft.Extensions.Hosting) instead of a third-party scheduler
/// like Hangfire. Every sweep: (1) looks for confirmed bookings starting within the reminder
/// window that haven't been reminded yet and emails the owner, and (2) purges old refresh tokens
/// so that table doesn't grow forever.
/// </summary>
public class BookingReminderService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BookingReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(2);
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenRetention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The TimeProvider overload lets a test drive a tick synchronously via FakeTimeProvider
        // (advance the fake clock past SweepInterval) instead of waiting on a real 5-minute timer.
        using var timer = new PeriodicTimer(SweepInterval, timeProvider);

        do
        {
            try
            {
                await SendDueRemindersAsync(stoppingToken);
                await CleanupExpiredRefreshTokensAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background maintenance sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookItDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now.Add(ReminderWindow);

        var dueBookings = await db.Bookings
            .Include(b => b.Resource)
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.ReminderSentAtUtc == null
                        && b.StartUtc <= cutoff
                        && b.StartUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var booking in dueBookings)
        {
            var user = await userManager.FindByIdAsync(booking.UserId.ToString());
            if (user?.Email is null)
            {
                continue;
            }

            var subject = $"Reminder: {booking.Resource?.Name} at {booking.StartUtc:t}";
            var body = $"Your booking {booking.ReferenceCode} for {booking.Resource?.Name} starts at {booking.StartUtc:g} UTC.";

            await emailSender.SendAsync(user.Email, subject, body, cancellationToken);
            booking.MarkReminderSent(now);
        }

        if (dueBookings.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Sent {Count} booking reminder(s).", dueBookings.Count);
        }
    }

    private async Task CleanupExpiredRefreshTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookItDbContext>();

        var cutoff = timeProvider.GetUtcNow().UtcDateTime.Subtract(TokenRetention);

        // ExecuteDeleteAsync issues a single DELETE ... WHERE statement — no loading rows into
        // the change tracker just to remove them. Rows are kept for a short retention window
        // after expiry/revocation (not deleted immediately) purely so a reuse-detection incident
        // has a trail to inspect for a few days.
        var deleted = await db.RefreshTokens
            .Where(t => t.ExpiresAtUtc < cutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} expired/revoked refresh token(s).", deleted);
        }
    }
}
