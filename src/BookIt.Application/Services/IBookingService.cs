using BookIt.Application.Common;
using BookIt.Application.Dtos;

namespace BookIt.Application.Services;

public interface IBookingService
{
    Task<Result<BookingDto>> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken = default);
    Task<Result<BookingDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<BookingDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<BookingDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<AvailabilityResponse>> GetAvailabilityAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default);

    Task<Result<BookingDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<BookingDto>> CheckInAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<BookingDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<BookingDto>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken = default);
}
