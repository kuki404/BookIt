using System.Linq.Expressions;
using BookIt.Application.Dtos;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;

namespace BookIt.Application.Mapping;

/// <summary>
/// EF Core translates this expression tree into the SELECT column list — the DB never sends
/// columns the DTO doesn't need, and no full entity graph is materialized just to be reshaped in
/// memory afterwards. Kept in Application (not Infrastructure) so it has zero EF Core dependency
/// and stays reusable/unit-testable as plain LINQ.
/// </summary>
public static class ResourceProjections
{
    public static readonly Expression<Func<Resource, ResourceDto>> ToDto = r => new ResourceDto(
        r.Id,
        r.Name,
        r.Description,
        r.Type,
        r.Type.ToDisplayText(),
        r.Capacity,
        r.IsActive);
}
