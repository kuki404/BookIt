using BookIt.Domain.Enums;
using MudBlazor;

namespace BookIt.Web.ViewModels;

/// <summary>
/// UI-shaped counterpart to <see cref="BookIt.Application.Dtos.BookingDto"/> — carries display-only
/// fields (chip color, a pre-formatted time range) that have no business meaning and so don't
/// belong on the API contract. Mapster (<c>MapsterConfig</c>) fills these in on the way from DTO
/// to view-model; the API/Application layer never sees this type.
/// </summary>
public class BookingViewModel
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public BookingStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }

    public Color StatusColor { get; set; }
    public string TimeRangeDisplay { get; set; } = string.Empty;
}
