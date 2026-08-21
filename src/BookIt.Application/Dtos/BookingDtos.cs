using System.ComponentModel.DataAnnotations;
using BookIt.Domain.Enums;

namespace BookIt.Application.Dtos;

public record BookingDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    Guid UserId,
    string ReferenceCode,
    DateTime StartUtc,
    DateTime EndUtc,
    BookingStatus Status,
    string StatusDisplay,
    string? Notes,
    string? CancellationReason);

public record CreateBookingRequest(
    [Required] Guid ResourceId,
    [Required] DateTime StartUtc,
    [Required] DateTime EndUtc,
    [MaxLength(1000)] string? Notes) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartUtc >= EndUtc)
        {
            yield return new ValidationResult(
                "Start time must be before end time.",
                [nameof(StartUtc), nameof(EndUtc)]);
        }
    }
}

public record CancelBookingRequest([MaxLength(500)] string? Reason);

public record BookingSlotDto(DateTime StartUtc, DateTime EndUtc, string StatusDisplay);

public record AvailabilityResponse(Guid ResourceId, DateOnly Date, IReadOnlyList<BookingSlotDto> BookedSlots);

public record AvailabilityRangeResponse(Guid ResourceId, IReadOnlyList<AvailabilityResponse> Days);
