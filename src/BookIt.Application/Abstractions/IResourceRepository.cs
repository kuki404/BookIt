using BookIt.Domain.Entities;

namespace BookIt.Application.Abstractions;

public interface IResourceRepository
{
    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Resource>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task AddAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
