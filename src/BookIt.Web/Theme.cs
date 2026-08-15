using MudBlazor;

namespace BookIt.Web;

/// <summary>Custom palette (teal/indigo) so the app doesn't read as an out-of-the-box MudBlazor demo.</summary>
public static class BookItTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0F766E",
            Secondary = "#7C3AED",
            AppbarBackground = "#0F766E",
            Background = "#F8FAFC",
            Surface = "#FFFFFF"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#2DD4BF",
            Secondary = "#A78BFA",
            AppbarBackground = "#0B1220",
            Background = "#0B1220",
            Surface = "#111827"
        }
    };
}
