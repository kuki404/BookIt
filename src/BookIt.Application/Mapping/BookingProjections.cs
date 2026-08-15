using System.Linq.Expressions;
using BookIt.Application.Dtos;
using BookIt.Domain.Common;
using BookIt.Domain.Entities;

namespace BookIt.Application.Mapping;

/// <summary>SQL-side projection for Booking — see <see cref="ResourceProjections"/> for why this lives here instead of mapping loaded entities.</summary>
public static class BookingProjections
{
    public static readonly Expression<Func<Booking, BookingDto>> ToDto = b => new BookingDto(
        b.Id,
        b.ResourceId,
        b.Resource!.Name,
        b.UserId,
        b.ReferenceCode,
        b.StartUtc,
        b.EndUtc,
        b.Status,
        b.Status.ToDisplayText(),
        b.Notes,
        b.CancellationReason);
}
