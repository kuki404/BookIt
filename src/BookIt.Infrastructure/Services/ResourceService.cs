using BookIt.Application.Common;
using BookIt.Application.Dtos;
using BookIt.Application.Mapping;
using BookIt.Application.Services;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;
using BookIt.Infrastructure.Caching;
using BookIt.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Services;

/// <summary>
/// Talks to <see cref="BookItDbContext"/> directly instead of through a repository — the DbSet
/// already is a repository and the DbContext already is a unit of work, so a wrapping interface
/// would only hide the EF Core features (projection, AsNoTracking, paging) this class relies on.
/// </summary>
public class ResourceService(BookItDbContext db, ResourceCache cache) : IResourceService
{
    public ValueTask<PagedResult<ResourceDto>> GetAllAsync(bool includeInactive, PagedRequest paging, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateListAsync(includeInactive, paging, ct => new ValueTask<PagedResult<ResourceDto>>(QueryAsync(includeInactive, paging, ct)), cancellationToken);

    private Task<PagedResult<ResourceDto>> QueryAsync(bool includeInactive, PagedRequest paging, CancellationToken cancellationToken)
    {
        // Read-only listing: AsNoTracking skips change-tracking bookkeeping EF Core would
        // otherwise set up for every row, for no benefit since nothing here gets updated.
        var query = db.Resources.AsNoTracking().OrderBy(r => r.Name).AsQueryable();
        if (!includeInactive)
        {
            query = query.ActiveOnly();
        }

        return query.ToPagedResultAsync(ResourceProjections.ToDto, paging, cancellationToken);
    }

    public async Task<Result<ResourceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await db.Resources.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ResourceProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken);

        return dto is null ? Result<ResourceDto>.Failure("Resource not found.") : Result<ResourceDto>.Success(dto);
    }

    public async Task<ResourceDto> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken = default)
    {
        var resource = Resource.Create(request.Name, request.Type, request.Capacity, request.Description);
        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);

        // The catalog just changed — drop the cached listing rather than let it serve stale data
        // until its TTL expires.
        await cache.InvalidateAsync(cancellationToken);

        return new ResourceDto(resource.Id, resource.Name, resource.Description, resource.Type, resource.Type.ToDisplayText(), resource.Capacity, resource.IsActive);
    }

    public async Task<Result<ResourceDto>> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken = default)
    {
        // Tracked on purpose here: this is a write, so EF Core needs to know what changed.
        var resource = await db.Resources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (resource is null)
        {
            return Result<ResourceDto>.Failure("Resource not found.");
        }

        resource.Update(request.Name, request.Description, request.Capacity);
        await db.SaveChangesAsync(cancellationToken);
        await cache.InvalidateAsync(cancellationToken);

        return Result<ResourceDto>.Success(new ResourceDto(resource.Id, resource.Name, resource.Description, resource.Type, resource.Type.ToDisplayText(), resource.Capacity, resource.IsActive));
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // ExecuteUpdateAsync issues a single UPDATE ... SET IsActive = 0 statement — no SELECT to
        // load the row first, no change tracker entry, just the write that's actually needed.
        var affected = await db.Resources
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.IsActive, false), cancellationToken);

        if (affected == 0)
        {
            return Result.Failure("Resource not found.");
        }

        await cache.InvalidateAsync(cancellationToken);
        return Result.Success();
    }
}
