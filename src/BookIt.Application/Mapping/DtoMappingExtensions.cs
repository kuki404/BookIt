using BookIt.Domain.Common;
using BookIt.Domain.Entities;
using BookIt.Application.Dtos;

namespace BookIt.Application.Mapping;

public static class DtoMappingExtensions
{
    public static ResourceDto ToDto(this Resource resource) => new(
        resource.Id,
        resource.Name,
        resource.Description,
        resource.Type,
        resource.Type.ToDisplayText(),
        resource.Capacity,
        resource.IsActive);

    public static BookingDto ToDto(this Booking booking) => new(
        booking.Id,
        booking.ResourceId,
        booking.Resource?.Name ?? string.Empty,
        booking.UserId,
        booking.ReferenceCode,
        booking.StartUtc,
        booking.EndUtc,
        booking.Status,
        booking.Status.ToDisplayText(),
        booking.Notes,
        booking.CancellationReason);
}
