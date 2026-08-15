using System.Data;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Mapping;
using BookIt.Application.Services;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;
using BookIt.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Services;

public class BookingService(BookItDbContext db) : IBookingService
{
    // Compiled once, reused on every call — this exact shape (overlap check for a resource/time
    // range) runs on every booking attempt, including retries under contention, so it's the one
    // query in this project worth bypassing LINQ's per-call expression-tree compilation for.
    private static readonly Func<BookItDbContext, Guid, DateTime, DateTime, Task<bool>> CompiledHasOverlapQuery =
        EF.CompileAsyncQuery((BookItDbContext ctx, Guid resourceId, DateTime startUtc, DateTime endUtc) =>
            ctx.Bookings.Any(b =>
                b.ResourceId == resourceId &&
                b.Status != BookingStatus.Cancelled &&
                b.StartUtc < endUtc &&
                startUtc < b.EndUtc));

    public async Task<Result<BookingDto>> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        var resourceIsBookable = await db.Resources.AsNoTracking()
            .AnyAsync(r => r.Id == request.ResourceId && r.IsActive, cancellationToken);
        if (!resourceIsBookable)
        {
            return Result<BookingDto>.Failure("Resource not found or inactive.");
        }

        Booking booking;
        try
        {
            booking = Booking.Create(request.ResourceId, userId, request.StartUtc, request.EndUtc, request.Notes);
        }
        catch (DomainException ex)
        {
            return Result<BookingDto>.Failure(ex.Message);
        }

        var added = await TryAddWithOverlapCheckAsync(booking, cancellationToken);
        if (!added)
        {
            return Result<BookingDto>.Failure("This resource is already booked for the requested time range.");
        }

        var dto = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == booking.Id)
            .Select(BookingProjections.ToDto)
            .FirstAsync(cancellationToken);

        return Result<BookingDto>.Success(dto);
    }

    /// <summary>
    /// Runs the overlap check and the insert inside one Serializable transaction so two
    /// concurrent requests for the same resource/time-slot can't both pass the check and both
    /// insert (a classic check-then-act race) — the loser blocks, then fails against the row the
    /// winner just committed instead of silently double-booking.
    /// </summary>
    private async Task<bool> TryAddWithOverlapCheckAsync(Booking booking, CancellationToken cancellationToken)
    {
        // EnableRetryOnFailure() requires manual transactions to run through the execution
        // strategy, so a transient-fault retry replays the whole check-then-insert unit instead
        // of resuming with a half-open transaction.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var overlaps = await CompiledHasOverlapQuery(db, booking.ResourceId, booking.StartUtc, booking.EndUtc);
            if (overlaps)
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

    public Task<Result<BookingDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.Confirm(), cancellationToken);

    public Task<Result<BookingDto>> CheckInAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.CheckIn(), cancellationToken);

    public Task<Result<BookingDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.Complete(), cancellationToken);

    public Task<Result<BookingDto>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(id, b => b.Cancel(reason), cancellationToken);

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
