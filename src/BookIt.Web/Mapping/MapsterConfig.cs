using BookIt.Application.Dtos;
using BookIt.Domain.Enums;
using BookIt.Web.ViewModels;
using Mapster;
using MudBlazor;

namespace BookIt.Web.Mapping;

/// <summary>
/// Registers DTO → ViewModel mappings once at startup (see Program.cs) so every page calls the
/// same `.Adapt&lt;T&gt;()` extension instead of hand-rolling the same field-by-field copy
/// everywhere. Only the display-only fields need explicit `.Map(...)` calls below — Mapster
/// copies same-named properties (Id, Name, StatusDisplay, ...) automatically by convention.
/// </summary>
public static class MapsterConfig
{
    public static void Configure()
    {
        TypeAdapterConfig<BookingDto, BookingViewModel>.NewConfig()
            .Map(dest => dest.StatusColor, src => ToStatusColor(src.Status))
            .Map(dest => dest.TimeRangeDisplay, src => $"{src.StartUtc:g} – {src.EndUtc:t}");

        TypeAdapterConfig<ResourceDto, ResourceViewModel>.NewConfig()
            .Map(dest => dest.StatusColor, src => src.IsActive ? Color.Success : Color.Default)
            .Map(dest => dest.TypeIcon, src => ToTypeIcon(src.Type));
    }

    private static Color ToStatusColor(BookingStatus status) => status switch
    {
        BookingStatus.Pending => Color.Warning,
        BookingStatus.Confirmed => Color.Primary,
        BookingStatus.CheckedIn => Color.Info,
        BookingStatus.Completed => Color.Success,
        BookingStatus.Cancelled => Color.Default,
        _ => Color.Default
    };

    private static string ToTypeIcon(ResourceType type) => type switch
    {
        ResourceType.Room => Icons.Material.Filled.MeetingRoom,
        ResourceType.Equipment => Icons.Material.Filled.Build,
        ResourceType.Service => Icons.Material.Filled.RoomService,
        _ => Icons.Material.Filled.Inventory2
    };
}
