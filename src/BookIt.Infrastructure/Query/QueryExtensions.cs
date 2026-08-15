using System.Linq.Expressions;
using BookIt.Application.Common;
using BookIt.Domain.Entities;
using BookIt.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Infrastructure.Query;

/// <summary>
/// Small, named `IQueryable` filters — the DRY benefit of a repository's query methods, without
/// a repository class hiding the `IQueryable` (and with it, the ability to compose/project/page
/// EF Core queries however a given call site needs).
/// </summary>
public static class QueryExtensions
{
    public static IQueryable<Resource> ActiveOnly(this IQueryable<Resource> query) => query.Where(r => r.IsActive);

    public static IQueryable<Booking> NotCancelled(this IQueryable<Booking> query) => query.Where(b => b.Status != BookingStatus.Cancelled);

    public static IQueryable<Booking> ForUser(this IQueryable<Booking> query, Guid userId) => query.Where(b => b.UserId == userId);

    /// <summary>
    /// Runs a COUNT + a projected, paged SELECT — never both against a tracked/materialized
    /// entity set — so list endpoints only ever pull the columns and rows they'll actually return.
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TEntity, TDto>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, TDto>> projection,
        PagedRequest paging,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<TDto>(items, totalCount, paging.Page, paging.PageSize);
    }
}
