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
/// Recurring reminder job built on the framework's own <see cref="BackgroundService"/> +
/// <see cref="PeriodicTimer"/> (Microsoft.Extensions.Hosting) instead of a third-party scheduler
/// like Hangfire. Every sweep looks for confirmed bookings starting within the reminder window
/// that haven't been reminded yet, and emails the owner.
/// </summary>
public class BookingReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<BookingReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        do
        {
            try
            {
                await SendDueRemindersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Booking reminder sweep failed.");
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

        var cutoff = DateTime.UtcNow.Add(ReminderWindow);

        var dueBookings = await db.Bookings
            .Include(b => b.Resource)
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.ReminderSentAtUtc == null
                        && b.StartUtc <= cutoff
                        && b.StartUtc > DateTime.UtcNow)
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
            booking.MarkReminderSent();
        }

        if (dueBookings.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Sent {Count} booking reminder(s).", dueBookings.Count);
        }
    }
}
