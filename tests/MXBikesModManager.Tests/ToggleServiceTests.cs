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
    public async Task Toggling_normalises_a_paired_folder_that_is_out_of_sync()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Bike_D.pkz");
        env.MakeDirectory("bikes/Bike_D.disabled");

        var cache = new MetadataCache(env.Config.Data.Cache, env.Config.Log);
        var result = await new ModScanner(env.Config, cache).ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        var entry = result.Entries.Single(e => e.BaseName == "Bike_D" && e.IsEnabled);
        Assert.NotNull(entry.PairedFolderPath);

        var toggle = new ToggleService(env.Config);
        Assert.True(toggle.SetEnabled(entry, false).Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_D.pkz.disabled")));
        Assert.True(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_D.disabled")));

        // Enabling again moves only the folder, which is the part that was renamed.
        Assert.True(toggle.SetEnabled(entry, true).Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_D.pkz")));
        Assert.True(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_D")));
    }

    [Fact]
    public async Task Toggling_normalises_an_enabled_folder_paired_with_a_disabled_archive()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Bike_E.pkz.disabled");
        env.MakeDirectory("bikes/Bike_E");

        var cache = new MetadataCache(env.Config.Data.Cache, env.Config.Log);
        var result = await new ModScanner(env.Config, cache).ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        var entry = result.Entries.Single(e => e.BaseName == "Bike_E");
        Assert.False(entry.IsEnabled);
        Assert.NotNull(entry.PairedFolderPath);

        Assert.True(new ToggleService(env.Config).SetEnabled(entry, true).Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_E.pkz")));
        Assert.True(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "Bike_E")));
    }

    [Fact]
    public async Task Trash_path_does_not_split_categories_that_contain_slashes()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/MX1OEM_2023_KTM_450_SX-F.pkz", "payload");

        var cache = new MetadataCache(env.Config.Data.Cache, env.Config.Log);
        var result = await new ModScanner(env.Config, cache).ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        var entry = result.Entries.Single(e => e.BaseName.StartsWith("MX1OEM"));
        Assert.Equal("MX1 250/450", entry.Category);

        var moved = new ToggleService(env.Config).MoveToTrash(entry);
        Assert.True(moved.Success);

        var recovered = Directory.GetFiles(env.Config.Data.Trash, "*.pkz", SearchOption.AllDirectories);
        Assert.Single(recovered);
        Assert.Contains("MX1 250-450", recovered[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Toggling_folder_only_mods_renames_the_folder()
    {
        using var env = new TestEnvironment();
        var folder = env.MakeDirectory("tyres/Loose_Tyre");
        var entry = new ModEntry
        {
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
