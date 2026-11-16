using System.Text.RegularExpressions;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class ThemeServiceTests
{
    [Fact]
    public void Five_palettes_are_available_with_unique_keys()
    {
        Assert.Equal(5, ThemeService.Palettes.Count);
        Assert.Equal(
            ThemeService.Palettes.Count,
            ThemeService.Palettes.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(ThemeService.Palettes, p => p.Key == "midnight");
        Assert.Contains(ThemeService.Palettes, p => p.Key == "forest");
        Assert.Contains(ThemeService.Palettes, p => p.Key == "violet");
        Assert.Contains(ThemeService.Palettes, p => p.Key == "mono");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void Unknown_theme_falls_back_to_ember(string? key)
    {
        Assert.Equal(ThemeService.DefaultKey, ThemeService.Resolve(key).Key);
        Assert.False(ThemeService.IsKnown(key));
    }

    [Fact]
    public void Theme_keys_are_case_insensitive()
    {
        Assert.Equal("midnight", ThemeService.Resolve("MIDNIGHT").Key);
        Assert.True(ThemeService.IsKnown("Forest"));
    }

    [Fact]
    public void Every_palette_defines_the_same_complete_resource_set()
    {
        var expected = ThemeService.BuildResources(ThemeService.Default).Keys.ToHashSet();

        Assert.Contains("AppBg", expected);
        Assert.Contains("AccentBrush", expected);
        Assert.Contains("ChipBg", expected);
        Assert.Contains("TargetBg", expected);
        Assert.Contains("WarningText", expected);
        Assert.Contains("OverlayScrim", expected);
        Assert.Contains("SystemAccentColor", expected);
        Assert.Contains("SystemAccentColorLight3", expected);

        foreach (var palette in ThemeService.Palettes)
        {
            var actual = ThemeService.BuildResources(palette).Keys.ToHashSet();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Every_palette_colour_is_a_valid_hex_value()
    {
        var hex = new Regex("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        foreach (var palette in ThemeService.Palettes)
        {
            foreach (var (key, value) in ThemeService.BuildResources(palette))
            {
                Assert.True(hex.IsMatch(value), $"{palette.Key}.{key} = {value}");
            }

            Assert.NotEqual(
                ThemeService.BuildResources(palette)["AccentBrush"],
                ThemeService.BuildResources(palette)["AppBg"]);
        }
    }

    [Fact]
    public void Palettes_change_the_background_and_the_accent()
    {
        var backgrounds = ThemeService.Palettes.Select(p => p.Background).Distinct().Count();
        var accents = ThemeService.Palettes.Select(p => p.Accent).Distinct().Count();

        Assert.Equal(ThemeService.Palettes.Count, backgrounds);
        Assert.Equal(ThemeService.Palettes.Count, accents);
    }

    [Fact]
    public void Default_config_uses_the_ember_theme()
    {
        var config = ConfigService.CreateDefault();

        Assert.Equal(ThemeService.DefaultKey, config.Ui.Theme);
    }

    [Fact]
    public void Theme_choice_round_trips_through_the_config_file()
    {
        using var temp = new TestDir();
        var path = Path.Combine(temp.Path, "appsettings.json");
        var config = ConfigService.Load(path);
        config.Config.Game.AutoDetect = false;
        config.Config.Ui.Theme = "forest";

        Assert.True(config.Save());

        var reloaded = ConfigService.Load(path);
        Assert.Equal("forest", reloaded.Config.Ui.Theme);
    }
}
