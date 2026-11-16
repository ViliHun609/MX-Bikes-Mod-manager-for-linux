using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MXBikesModManager.Services;

/// <summary>
/// A dark colour scheme. Kept as plain hex strings so palettes stay easy to read and test;
/// <see cref="ThemeService"/> turns them into the DynamicResource brushes used by the views.
/// </summary>
public sealed record ThemePalette(
    string Key,
    string DisplayName,
    string Icon,
    string Tagline,
    string Background,
    string Panel,
    string PanelAlt,
    string Card,
    string CardHover,
    string Line,
    string Text,
    string Muted,
    string Accent,
    string AccentHover,
    string AccentText,
    string Danger,
    string Preview,
    string Chip,
    string TargetBg,
    string TargetBorder,
    string EmptyIcon,
    string TabCount);

/// <summary>Applies the selected dark palette to the Avalonia application resources.</summary>
public static class ThemeService
{
    public const string DefaultKey = "ember";

    /// <summary>Warning colours are shared; they need to stay legible on every dark background.</summary>
    private static readonly (string Key, string Value)[] SharedBrushes =
    {
        ("WarningBg", "#2A1C0E"),
        ("WarningBorder", "#6B3F10"),
        ("WarningText", "#FFCF9A"),
        ("BadgeScrim", "#B3000000"),
        ("OverlayScrim", "#D9000000")
    };

    public static IReadOnlyList<ThemePalette> Palettes { get; } = new[]
    {
        new ThemePalette(
            "ember", "Ember", "fire", "Warm orange",
            Background: "#0D1014", Panel: "#151A20", PanelAlt: "#11161B",
            Card: "#1B222A", CardHover: "#222B35", Line: "#28323D",
            Text: "#E9EFF5", Muted: "#8D9BAB",
            Accent: "#FF8A1E", AccentHover: "#FFA347", AccentText: "#1A1206",
            Danger: "#E0452F", Preview: "#0B0E12",
            Chip: "#141A21", TargetBg: "#151C24", TargetBorder: "#26313D",
            EmptyIcon: "#3B4A5A", TabCount: "#1C242E"),

        new ThemePalette(
            "midnight", "Midnight", "moon", "Cool blue",
            Background: "#0A0F17", Panel: "#111925", PanelAlt: "#0D1420",
            Card: "#16202E", CardHover: "#1D2938", Line: "#243244",
            Text: "#E7EEF7", Muted: "#8A9AB0",
            Accent: "#4C9AFF", AccentHover: "#74B2FF", AccentText: "#07121F",
            Danger: "#E2493A", Preview: "#080D14",
            Chip: "#111A26", TargetBg: "#0E1724", TargetBorder: "#22334A",
            EmptyIcon: "#33465E", TabCount: "#182432"),

        new ThemePalette(
            "forest", "Forest", "tree", "Deep green",
            Background: "#0A110E", Panel: "#111B16", PanelAlt: "#0D1511",
            Card: "#16241D", CardHover: "#1D2F26", Line: "#24352C",
            Text: "#E8F2EC", Muted: "#8BA398",
            Accent: "#43D18B", AccentHover: "#6BE0A7", AccentText: "#04150C",
            Danger: "#E0523F", Preview: "#070E0B",
            Chip: "#101B15", TargetBg: "#0E1A14", TargetBorder: "#223A2D",
            EmptyIcon: "#355447", TabCount: "#16251D"),

        new ThemePalette(
            "violet", "Violet", "gem", "Soft purple",
            Background: "#0D0A14", Panel: "#151020", PanelAlt: "#100C19",
            Card: "#1D162B", CardHover: "#261D38", Line: "#2E2540",
            Text: "#EFEAF7", Muted: "#9D92B3",
            Accent: "#A78BFA", AccentHover: "#C0ACFF", AccentText: "#120A24",
            Danger: "#E2485F", Preview: "#09070F",
            Chip: "#151022", TargetBg: "#120D1E", TargetBorder: "#322748",
            EmptyIcon: "#4B3E66", TabCount: "#1C1529"),

        new ThemePalette(
            "mono", "Mono", "mono", "Neutral slate",
            Background: "#101214", Panel: "#16191C", PanelAlt: "#121417",
            Card: "#1C2024", CardHover: "#252A2F", Line: "#2B3036",
            Text: "#E9EDF1", Muted: "#969FA8",
            Accent: "#C3CED8", AccentHover: "#D8E0E8", AccentText: "#12161A",
            Danger: "#E05A4A", Preview: "#0C0E10",
            Chip: "#171A1E", TargetBg: "#15191D", TargetBorder: "#2E343A",
            EmptyIcon: "#4A5259", TabCount: "#20252A")
    };

    public static ThemePalette Default => Palettes[0];

    /// <summary>The palette currently installed in the application resources.</summary>
    public static ThemePalette Current { get; private set; } = Default;
    public static ThemePalette Resolve(string? key) =>
        Palettes.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? Default;

    public static bool IsKnown(string? key) =>
        Palettes.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds the complete resource set for a palette (brushes plus Fluent accent colours).</summary>
    public static IReadOnlyDictionary<string, string> BuildResources(ThemePalette palette)
    {
        var accent = Color.Parse(palette.Accent);

        var resources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppBg"] = palette.Background,
            ["PanelBg"] = palette.Panel,
            ["PanelBgAlt"] = palette.PanelAlt,
            ["CardBg"] = palette.Card,
            ["CardHover"] = palette.CardHover,
            ["LineBrush"] = palette.Line,
            ["TextBrush"] = palette.Text,
            ["MutedBrush"] = palette.Muted,
            ["AccentBrush"] = palette.Accent,
            ["AccentHoverBrush"] = palette.AccentHover,
            ["AccentTextBrush"] = palette.AccentText,
            ["DangerBrush"] = palette.Danger,
            ["PreviewBg"] = palette.Preview,
            ["ChipBg"] = palette.Chip,
            ["TargetBg"] = palette.TargetBg,
            ["TargetBorder"] = palette.TargetBorder,
            ["EmptyIconBrush"] = palette.EmptyIcon,
            ["TabCountBg"] = palette.TabCount,

            // Fluent controls (checkboxes, focus rings, selection) follow these.
            ["SystemAccentColor"] = palette.Accent,
            ["SystemAccentColorDark1"] = ToHex(Scale(accent, 0.88)),
            ["SystemAccentColorDark2"] = ToHex(Scale(accent, 0.76)),
            ["SystemAccentColorDark3"] = ToHex(Scale(accent, 0.64)),
            ["SystemAccentColorLight1"] = ToHex(Lighten(accent, 0.15)),
            ["SystemAccentColorLight2"] = ToHex(Lighten(accent, 0.30)),
            ["SystemAccentColorLight3"] = ToHex(Lighten(accent, 0.45))
        };

        foreach (var (key, value) in SharedBrushes)
        {
            resources[key] = value;
        }

        return resources;
    }

    /// <summary>
    /// Swaps the application palette. Replacing the merged dictionary raises ResourcesChanged,
    /// so every DynamicResource in every open window updates immediately.
    /// </summary>
    public static void Apply(string? key)
    {
        var app = Application.Current;
        if (app is null) return;

        var palette = Resolve(key);
        var dictionary = new ResourceDictionary();
        foreach (var (resourceKey, value) in BuildResources(palette))
        {
            if (resourceKey.StartsWith("SystemAccentColor", StringComparison.Ordinal))
            {
                dictionary[resourceKey] = Color.Parse(value);
            }
            else
            {
                dictionary[resourceKey] = new SolidColorBrush(Color.Parse(value));
            }
        }

        var merged = app.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dictionary);
        Current = palette;
    }

    private static Color Scale(Color color, double factor) => Color.FromArgb(
        color.A,
        (byte)Math.Round(color.R * factor),
        (byte)Math.Round(color.G * factor),
        (byte)Math.Round(color.B * factor));

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Round(color.R + (255 - color.R) * amount),
        (byte)Math.Round(color.G + (255 - color.G) * amount),
        (byte)Math.Round(color.B + (255 - color.B) * amount));

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
