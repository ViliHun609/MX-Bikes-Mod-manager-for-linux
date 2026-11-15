using MXBikesModManager.Models;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class ToggleServiceTests
{
    private static async Task<ModEntry> ScanOne(TestEnvironment env, string baseName)
    {
        var cache = new MetadataCache(env.Config.Data.Cache, env.Config.Log);
        var scanner = new ModScanner(env.Config, cache);
        var result = await scanner.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        return result.Entries.Single(e => e.BaseName == baseName && e.IsEnabled);
    }

    [Fact]
    public async Task Disabling_renames_pkz_and_paired_folder_then_reenabling_restores()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Bike_A.pkz");
        env.Touch("bikes/Bike_A/paints/livery.pnt");

        var entry = await ScanOne(env, "Bike_A");
        var toggle = new ToggleService(env.Config);

        var off = toggle.SetEnabled(entry, false);
        Assert.True(off.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_A.pkz.disabled")));
        Assert.True(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_A.disabled")));
        Assert.False(entry.IsEnabled);

        var on = toggle.SetEnabled(entry, true);
        Assert.True(on.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_A.pkz")));
        Assert.True(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_A")));
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_existing_disabled_copy()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Bike_B.pkz");
        env.Touch("bikes/Bike_B.pkz.disabled");

        var entry = await ScanOne(env, "Bike_B");
        var toggle = new ToggleService(env.Config);

        var result = toggle.SetEnabled(entry, false);

        Assert.False(result.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_B.pkz")));
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public async Task Move_to_trash_keeps_the_mod_recoverable()
    {
        using var env = new TestEnvironment();
        var path = env.Touch("bikes/Bike_C.pkz", "payload");

        var entry = await ScanOne(env, "Bike_C");
        var toggle = new ToggleService(env.Config);

        var result = toggle.MoveToTrash(entry);

        Assert.True(result.Success);
        Assert.False(File.Exists(path));
        var recovered = Directory.GetFiles(env.Config.Data.Trash, "Bike_C.pkz", SearchOption.AllDirectories);
        Assert.Single(recovered);
        Assert.Equal("payload", File.ReadAllText(recovered[0]));
    }

    [Fact]
    public void Toggling_folder_only_mods_renames_the_folder()
    {
        using var env = new TestEnvironment();
        var folder = env.MakeDirectory("tyres/Loose_Tyre");
        var entry = new ModEntry
        {
            Name = "Loose_Tyre",
            BaseName = "Loose_Tyre",
            Type = env.Type("tyres"),
            Kind = ModKind.Folder,
            Path = folder
        };

        var toggle = new ToggleService(env.Config);
        Assert.True(toggle.SetEnabled(entry, false).Success);
        Assert.True(Directory.Exists(folder + ".disabled"));
        Assert.True(toggle.SetEnabled(entry, true).Success);
        Assert.True(Directory.Exists(folder));
    }
}
