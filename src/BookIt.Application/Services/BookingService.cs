using BookIt.Application.Abstractions;
using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Mapping;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using BookIt.Domain.Exceptions;

namespace BookIt.Application.Services;

public class BookingService(IBookingRepository bookings, IResourceRepository resources) : IBookingService
{
    public async Task<Result<BookingDto>> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        var resource = await resources.GetByIdAsync(request.ResourceId, cancellationToken);
        if (resource is null || !resource.IsActive)
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

        var added = await bookings.TryAddWithOverlapCheckAsync(booking, cancellationToken);
        if (!added)
        {
            return Result<BookingDto>.Failure("This resource is already booked for the requested time range.");
        }

        booking = (await bookings.GetByIdAsync(booking.Id, cancellationToken))!;
        return Result<BookingDto>.Success(booking.ToDto());
    }

    public async Task<Result<BookingDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.GetByIdAsync(id, cancellationToken);
        return booking is null
            ? Result<BookingDto>.Failure("Booking not found.")
            : Result<BookingDto>.Success(booking.ToDto());
    }

    public async Task<List<BookingDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var list = await bookings.GetForUserAsync(userId, cancellationToken);
        return list.Select(b => b.ToDto()).ToList();
    }

    public async Task<List<BookingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await bookings.GetAllAsync(cancellationToken);
        return list.Select(b => b.ToDto()).ToList();
    }

    public async Task<Result<AvailabilityResponse>> GetAvailabilityAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var resource = await resources.GetByIdAsync(resourceId, cancellationToken);
        if (resource is null)
        {
            return Result<AvailabilityResponse>.Failure("Resource not found.");
        }

        var dayBookings = await bookings.GetForResourceAndDateAsync(resourceId, date, cancellationToken);
        var slots = dayBookings
            .Where(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.CheckedIn)
            .Select(b => new BookingSlotDto(b.StartUtc, b.EndUtc, b.Status.ToDisplayText()))
            .OrderBy(s => s.StartUtc)
            .ToList();

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

    private async Task<Result<BookingDto>> ApplyTransitionAsync(Guid id, Action<Booking> transition, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(id, cancellationToken);
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

        await bookings.SaveChangesAsync(cancellationToken);
        return Result<BookingDto>.Success(booking.ToDto());
    }
}
