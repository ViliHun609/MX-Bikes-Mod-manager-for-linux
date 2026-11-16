using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class AppIconsTests
{
    [Fact]
    public void Every_icon_has_non_empty_path_data()
    {
        foreach (var key in AppIcons.Keys)
        {
            var path = AppIcons.ResolvePath(key);
            Assert.False(string.IsNullOrWhiteSpace(path), key);
            Assert.True(path.Length > 20, key);
        }
    }

    [Fact]
    public void Known_type_icons_resolve_to_distinct_paths()
    {
        var keys = new[] { "bike", "track", "tyre", "rider", "misc", "paint" };
        var paths = keys.Select(AppIcons.ResolvePath).ToList();

        Assert.Equal(keys.Length, paths.Distinct().Count());
    }

    [Fact]
    public void Unknown_icon_falls_back_to_the_box()
    {
        var fallback = AppIcons.ResolvePath("misc");

        Assert.Equal(fallback, AppIcons.ResolvePath("not-an-icon"));
        Assert.Equal(fallback, AppIcons.ResolvePath(null));
        Assert.Equal(fallback, AppIcons.ResolvePath(string.Empty));
    }

    [Fact]
    public void Old_emoji_config_values_still_resolve()
    {
        Assert.Equal(AppIcons.ResolvePath("bike"), AppIcons.ResolvePath("🏍"));
        Assert.Equal(AppIcons.ResolvePath("rider"), AppIcons.ResolvePath("🧑"));
        Assert.Equal(AppIcons.ResolvePath("rider"), AppIcons.ResolvePath("🧑‍🦱"));
        Assert.Equal(AppIcons.ResolvePath("paint"), AppIcons.ResolvePath("🖌"));
    }

    [Fact]
    public void Default_types_and_themes_use_known_icon_keys()
    {
        var config = ConfigService.CreateDefault();
        foreach (var type in config.ModTypes)
        {
            Assert.True(AppIcons.IsKnown(type.Icon), $"{type.Key}: {type.Icon}");
        }

        foreach (var palette in ThemeService.Palettes)
        {
            Assert.True(AppIcons.IsKnown(palette.Icon), $"{palette.Key}: {palette.Icon}");
        }
    }
}
