using System.Data;
using BookIt.Application.Abstractions;
using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Repositories;

public class BookingRepository(BookItDbContext db) : IBookingRepository
{
    public Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Bookings.Include(b => b.Resource).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<Booking?> GetByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken = default) =>
        db.Bookings.Include(b => b.Resource).FirstOrDefaultAsync(b => b.ReferenceCode == referenceCode, cancellationToken);

    public Task<bool> HasOverlapAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
        HasOverlapCoreAsync(db, resourceId, startUtc, endUtc, cancellationToken);

    public async Task<bool> TryAddWithOverlapCheckAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        // EnableRetryOnFailure() requires manual transactions to run through the execution
        // strategy, so a transient-fault retry replays the whole check-then-insert unit instead
        // of resuming with a half-open transaction.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Serializable so two concurrent requests for the same resource/time-slot can't both
            // read "no overlap" before either has inserted — the second transaction is forced to
            // wait, then fails/retries against the now-visible row instead of silently double-booking.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var overlaps = await HasOverlapCoreAsync(db, booking.ResourceId, booking.StartUtc, booking.EndUtc, cancellationToken);
            if (overlaps)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await db.Bookings.AddAsync(booking, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<List<Booking>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Bookings.Include(b => b.Resource)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.StartUtc)
            .ToListAsync(cancellationToken);

    public async Task<List<Booking>> GetForResourceAndDateAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        return await db.Bookings
            .Where(b => b.ResourceId == resourceId && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Bookings.Include(b => b.Resource)
            .OrderByDescending(b => b.StartUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await db.Bookings.AddAsync(booking, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    private static Task<bool> HasOverlapCoreAsync(BookItDbContext db, Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(b =>
            b.ResourceId == resourceId &&
            b.Status != BookingStatus.Cancelled &&
            b.StartUtc < endUtc &&
            startUtc < b.EndUtc,
            cancellationToken);
}
