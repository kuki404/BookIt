namespace BookIt.Application.Common;

/// <summary>Page of results + the total count, so the client can render "Page 2 of 7" without a second round-trip.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
