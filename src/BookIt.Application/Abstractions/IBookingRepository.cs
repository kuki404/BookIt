using BookIt.Domain.Entities;

namespace BookIt.Application.Abstractions;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Booking?> GetByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken = default);
    Task<bool> HasOverlapAsync(Guid resourceId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the overlap check and the insert inside a single serializable transaction, so two
    /// concurrent requests for the same resource/time-slot can't both pass the check and both
    /// insert (a classic check-then-act race). Returns false — without inserting — if an overlap
    /// is found.
    /// </summary>
    Task<bool> TryAddWithOverlapCheckAsync(Booking booking, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetForResourceAndDateAsync(Guid resourceId, DateOnly date, CancellationToken cancellationToken = default);
    Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
