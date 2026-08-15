using BookIt.Domain.Enums;
using MudBlazor;

namespace BookIt.Web.ViewModels;

/// <summary>UI-shaped counterpart to <see cref="BookIt.Application.Dtos.ResourceDto"/> — see <see cref="BookingViewModel"/> for why this exists.</summary>
public class ResourceViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ResourceType Type { get; set; }
    public string TypeDisplay { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; }

    public string TypeIcon { get; set; } = string.Empty;
    public Color StatusColor { get; set; }
}
