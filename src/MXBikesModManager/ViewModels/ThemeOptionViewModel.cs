using Avalonia.Media;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

/// <summary>A selectable theme card in the settings window.</summary>
public sealed class ThemeOptionViewModel
{
    public ThemeOptionViewModel(ThemePalette palette)
    {
        Palette = palette;
        BackgroundBrush = new SolidColorBrush(Color.Parse(palette.Background));
        PanelBrush = new SolidColorBrush(Color.Parse(palette.Panel));
        AccentBrush = new SolidColorBrush(Color.Parse(palette.Accent));
    }

    public ThemePalette Palette { get; }
    public string Key => Palette.Key;
    public string DisplayName => Palette.DisplayName;
    public Geometry IconGeometry => AppIcons.Resolve(Palette.Icon);
    public string Tagline => Palette.Tagline;

    public IBrush BackgroundBrush { get; }
    public IBrush PanelBrush { get; }
    public IBrush AccentBrush { get; }
}
