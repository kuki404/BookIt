using BookIt.Domain.Enums;

namespace BookIt.Domain.Common;

/// <summary>Display-text helpers, kept as plain extension methods instead of pulling in a smart-enum library.</summary>
public static class EnumExtensions
{
    public static string ToDisplayText(this BookingStatus status) => status switch
    {
        BookingStatus.Pending => "Pending",
        BookingStatus.Confirmed => "Confirmed",
        BookingStatus.CheckedIn => "Checked in",
        BookingStatus.Completed => "Completed",
        BookingStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string ToDisplayText(this ResourceType type) => type switch
    {
        ResourceType.Room => "Room",
        ResourceType.Equipment => "Equipment",
        ResourceType.Service => "Service",
        _ => type.ToString()
    };
}
