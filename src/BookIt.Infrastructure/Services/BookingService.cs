using System.Data;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Mapping;
using BookIt.Application.Services;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;
using BookIt.Infrastructure.Email;
using BookIt.Infrastructure.Identity;
using BookIt.Infrastructure.Query;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Services;

public class BookingService(BookItDbContext db, UserManager<ApplicationUser> userManager, IEmailSender emailSender, TimeProvider timeProvider) : IBookingService
{
    // Compiled once, reused on every call — this exact shape (overlap check for a resource/time
    // range) runs on every booking attempt, including retries under contention, so it's the one
    // query in this project worth bypassing LINQ's per-call expression-tree compilation for.
    // Counts overlaps rather than just checking Any() so a Resource.Capacity > 1 (a room with
    // multiple seats, a piece of equipment with several identical units) can hold that many
    // concurrent bookings instead of being treated as capacity 1.
    private static readonly Func<BookItDbContext, Guid, DateTime, DateTime, Task<int>> CompiledOverlapCountQuery =
        EF.CompileAsyncQuery((BookItDbContext ctx, Guid resourceId, DateTime startUtc, DateTime endUtc) =>
            ctx.Bookings.Count(b =>
                b.ResourceId == resourceId &&
                b.Status != BookingStatus.Cancelled &&
                b.StartUtc < endUtc &&
                startUtc < b.EndUtc));

    public async Task<Result<BookingDto>> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        var resource = await db.Resources.AsNoTracking()
            .Where(r => r.Id == request.ResourceId && r.IsActive)
            .Select(r => new { r.Capacity })
            .FirstOrDefaultAsync(cancellationToken);
        if (resource is null)
        {
            return Result<BookingDto>.Failure("Resource not found or inactive.");
        }

        Booking booking;
        try
        {
            booking = Booking.Create(request.ResourceId, userId, request.StartUtc, request.EndUtc, request.Notes, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            return Result<BookingDto>.Failure(ex.Message);
        }

        var added = await TryAddWithOverlapCheckAsync(booking, resource.Capacity, cancellationToken);
        if (!added)
        {
            return Result<BookingDto>.Failure("This resource is already booked for the requested time range.");
        }

        var dto = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == booking.Id)
            .Select(BookingProjections.ToDto)
            .FirstAsync(cancellationToken);

        await SendBookingEmailAsync(
            dto.UserId,
            $"Booking received: {dto.ResourceName}",
            $"Your booking {dto.ReferenceCode} for {dto.ResourceName} from {dto.StartUtc:g} to {dto.EndUtc:g} UTC has been received.",
            cancellationToken);

        return Result<BookingDto>.Success(dto);
    }

    // Fire-and-log, not fire-and-forget: a missed confirmation/cancellation email isn't worth
    // failing the booking/cancellation over (same tolerance as BookingReminderService), so a
    // lookup miss or SMTP failure is swallowed here rather than surfaced to the caller.
    private async Task SendBookingEmailAsync(Guid userId, string subject, string body, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user?.Email is null)
        {
            return;
        }

        await emailSender.SendAsync(user.Email, subject, body, cancellationToken);
    }

    /// <summary>
    /// Runs the overlap check and the insert inside one Serializable transaction so two
    /// concurrent requests for the same resource/time-slot can't both pass the check and both
    /// insert (a classic check-then-act race) — the loser blocks, then fails against the row the
    /// winner just committed instead of silently double-booking.
    /// </summary>
    private async Task<bool> TryAddWithOverlapCheckAsync(Booking booking, int capacity, CancellationToken cancellationToken)
    {
        // EnableRetryOnFailure() requires manual transactions to run through the execution
        // strategy, so a transient-fault retry replays the whole check-then-insert unit instead
        // of resuming with a half-open transaction.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var overlapCount = await CompiledOverlapCountQuery(db, booking.ResourceId, booking.StartUtc, booking.EndUtc);
            if (overlapCount >= capacity)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            db.Bookings.Add(booking);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<Result<BookingDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(BookingProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken);

        return dto is null ? Result<BookingDto>.Failure("Booking not found.") : Result<BookingDto>.Success(dto);
    }

    public async Task<Result<BookingDto>> GetByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken = default)
    {
        var normalized = referenceCode.Trim().ToUpperInvariant();

        var dto = await db.Bookings.AsNoTracking()
            .Where(b => b.ReferenceCode == normalized)
            .Select(BookingProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken);

        return dto is null ? Result<BookingDto>.Failure("No booking found for that reference code.") : Result<BookingDto>.Success(dto);
    }

    // TagWith stamps a SQL comment on the generated query — shows up in SQL Server's plan cache /
    // profiler output, so a slow-query report can be traced straight back to this call site.
    public Task<PagedResult<BookingDto>> GetForUserAsync(Guid userId, PagedRequest paging, CancellationToken cancellationToken = default) =>
        db.Bookings.AsNoTracking()
            .TagWith("BookingService.GetForUserAsync")
            .ForUser(userId)
            .OrderByDescending(b => b.StartUtc)
            .ToPagedResultAsync(BookingProjections.ToDto, paging, cancellationToken);

    public Task<PagedResult<BookingDto>> GetAllAsync(PagedRequest paging, CancellationToken cancellationToken = default) =>
        db.Bookings.AsNoTracking()
            .TagWith("BookingService.GetAllAsync (admin)")
            .OrderByDescending(b => b.StartUtc)
            .ToPagedResultAsync(BookingProjections.ToDto, paging, cancellationToken);

    public async Task<Result<AvailabilityResponse>> GetAvailabilityAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var resourceExists = await db.Resources.AsNoTracking().AnyAsync(r => r.Id == resourceId, cancellationToken);
        if (!resourceExists)
        {
            return Result<AvailabilityResponse>.Failure("Resource not found.");
        }

        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var slots = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == resourceId && b.StartUtc < dayEnd && b.EndUtc > dayStart)
            .Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingSlotDto(b.StartUtc, b.EndUtc, b.Status.ToDisplayText()))
            .ToListAsync(cancellationToken);

        return Result<AvailabilityResponse>.Success(new AvailabilityResponse(resourceId, date, slots));
    }

    private const int MaxRangeDays = 62;

    // Range-capable sibling of GetAvailabilityAsync for the resource calendar view: one query for
    // the whole window (same overlap shape as the single-day lookup and the compiled overlap
    // check above) instead of the caller looping day-by-day over the network.
    public async Task<Result<AvailabilityRangeResponse>> GetAvailabilityRangeAsync(Guid resourceId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            return Result<AvailabilityRangeResponse>.Failure("End date must not be before the start date.");
        }

        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxRangeDays)
        {
            return Result<AvailabilityRangeResponse>.Failure($"Range cannot exceed {MaxRangeDays} days.");
        }

        var resourceExists = await db.Resources.AsNoTracking().AnyAsync(r => r.Id == resourceId, cancellationToken);
        if (!resourceExists)
        {
            return Result<AvailabilityRangeResponse>.Failure("Resource not found.");
        }

        var rangeStart = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEnd = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);

        var slots = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == resourceId && b.StartUtc < rangeEnd && b.EndUtc > rangeStart)
            .Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
            .OrderBy(b => b.StartUtc)
            .Select(b => new BookingSlotDto(b.StartUtc, b.EndUtc, b.Status.ToDisplayText()))
            .ToListAsync(cancellationToken);

        var days = new List<AvailabilityResponse>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            var daySlots = slots.Where(s => s.StartUtc < dayEnd && s.EndUtc > dayStart).ToList();
            days.Add(new AvailabilityResponse(resourceId, date, daySlots));
        }

        return Result<AvailabilityRangeResponse>.Success(new AvailabilityRangeResponse(resourceId, days));
    }

    public Task<Result<BookingDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.Confirm(), cancellationToken);

    public Task<Result<BookingDto>> CheckInAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.CheckIn(), cancellationToken);

    public Task<Result<BookingDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.Complete(), cancellationToken);

    public async Task<Result<BookingDto>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var result = await ApplyTransitionAsync(id, b => b.Cancel(reason), cancellationToken);
        if (result.Succeeded)
        {
            await SendBookingEmailAsync(
                result.Value!.UserId,
                $"Booking cancelled: {result.Value.ResourceName}",
                $"Your booking {result.Value.ReferenceCode} for {result.Value.ResourceName} has been cancelled.",
                cancellationToken);
        }

        return result;
    }

    /// <summary>Loads the entity tracked (writes need change tracking) then delegates the actual state check to the domain method, so an invalid transition fails in one place regardless of which action triggered it.</summary>
    private async Task<Result<BookingDto>> ApplyTransitionAsync(Guid id, Action<Booking> transition, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(b => b.Resource).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null)
        {
            return Result<BookingDto>.Failure("Booking not found.");
        }

        try
        {
            transition(booking);
        }
        catch (DomainException ex)
        {
            return Result<BookingDto>.Failure(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result<BookingDto>.Success(new BookingDto(
            booking.Id, booking.ResourceId, booking.Resource?.Name ?? string.Empty, booking.UserId, booking.ReferenceCode,
            booking.StartUtc, booking.EndUtc, booking.Status, booking.Status.ToDisplayText(), booking.Notes, booking.CancellationReason));
    }
}
