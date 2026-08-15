using BookIt.Application.Abstractions;
using BookIt.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Repositories;

public class ResourceRepository(BookItDbContext db) : IResourceRepository
{
    public Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Resources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<List<Resource>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = db.Resources.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Resource resource, CancellationToken cancellationToken = default) =>
        await db.Resources.AddAsync(resource, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
