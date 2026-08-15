using BookIt.Application.Common;
using BookIt.Application.Dtos;
using Microsoft.Extensions.Caching.Hybrid;

namespace BookIt.Infrastructure.Caching;

/// <summary>
/// HybridCache (L1 in-memory + stampede protection) fronting the resource catalog — a small,
/// read-heavy, rarely-changing table hit on every page load. All entries share the "resources"
/// tag, so any write (create/update/deactivate) invalidates the whole catalog in one call instead
/// of guessing which cache keys might be stale.
/// </summary>
public class ResourceCache(HybridCache cache)
{
    private const string ResourcesTag = "resources";
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromMinutes(5) };

    public ValueTask<PagedResult<ResourceDto>> GetOrCreateListAsync(
        bool includeInactive,
        PagedRequest paging,
        Func<CancellationToken, ValueTask<PagedResult<ResourceDto>>> factory,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            $"resources:{includeInactive}:{paging.Page}:{paging.PageSize}",
            factory,
            Options,
            tags: [ResourcesTag],
            cancellationToken: cancellationToken);

    /// <summary>Called after any Create/Update/Deactivate — the only way stale catalog data would otherwise be served.</summary>
    public Task InvalidateAsync(CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(ResourcesTag, cancellationToken).AsTask();
}
