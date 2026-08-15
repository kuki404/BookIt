using System.ComponentModel.DataAnnotations;

namespace BookIt.Application.Common;

/// <summary>
/// Bound from query string (?page=&amp;pageSize=). PageSize is capped server-side (not just
/// documented) so a client can never force an unbounded "give me everything" query.
/// </summary>
public record PagedRequest([Range(1, int.MaxValue)] int Page = 1, [Range(1, 100)] int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;
}
