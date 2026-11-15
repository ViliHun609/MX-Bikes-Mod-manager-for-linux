using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class SteamLocatorTests
{
    [Fact]
    public void Finds_the_mods_folder_inside_a_proton_prefix()
    {
        using var temp = new TestDir();
        var steamApps = temp.Dir("Steam", "steamapps");
        var mods = temp.Dir("Steam", "steamapps", "compatdata", "655500", "pfx", "drive_c", "users",
            "steamuser", "My Documents", "PiBoSo", "MX Bikes", "mods");

        var result = SteamLocator.FindModsRoot("655500", new[] { steamApps });

        Assert.Equal(mods, result.ModsRoot);
        Assert.Single(result.Searched);
    }

    [Fact]
    public void Regression_compatdata_is_not_looked_up_under_a_second_steamapps_folder()
    {
        using var temp = new TestDir();
        var steamApps = temp.Dir("Steam", "steamapps");

        // The old buggy lookup path: steamapps/steamapps/compatdata/...
        temp.Dir("Steam", "steamapps", "steamapps", "compatdata", "655500", "pfx", "drive_c", "users",
            "steamuser", "My Documents", "PiBoSo", "MX Bikes", "mods");

        var result = SteamLocator.FindModsRoot("655500", new[] { steamApps });

        Assert.Null(result.ModsRoot);
    }

    [Fact]
    public void Prefix_lookup_is_case_insensitive()
    {
        using var temp = new TestDir();
        var steamApps = temp.Dir("Steam", "steamapps");
        var mods = temp.Dir("Steam", "steamapps", "compatdata", "655500", "pfx", "drive_c", "users",
            "steamuser", "my documents", "piboso", "mx bikes", "Mods");

        var result = SteamLocator.FindModsRoot("655500", new[] { steamApps });

        Assert.Equal(mods, result.ModsRoot);
    }

    [Fact]
    public void Finds_the_prefix_of_a_secondary_steam_library()
    {
        using var temp = new TestDir();
        var first = temp.Dir("Steam", "steamapps");
        var second = temp.Dir("Games", "SteamLibrary", "steamapps");
        var mods = temp.Dir("Games", "SteamLibrary", "steamapps", "compatdata", "655500", "pfx",
            "drive_c", "users", "steamuser", "Documents", "PiBoSo", "MX Bikes", "mods");

        var result = SteamLocator.FindModsRoot("655500", new[] { first, second });

        Assert.Equal(mods, result.ModsRoot);
        Assert.Equal(2, result.Searched.Count);
    }

    [Fact]
    public void Returns_null_when_the_game_was_never_started()
    {
        using var temp = new TestDir();
        var steamApps = temp.Dir("Steam", "steamapps");
        temp.Dir("Steam", "steamapps", "compatdata", "655500");

        var result = SteamLocator.FindModsRoot("655500", new[] { steamApps });

        Assert.Null(result.ModsRoot);
        Assert.Empty(result.Problems);
    }
}
