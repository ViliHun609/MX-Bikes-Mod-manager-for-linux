using System.IO.Compression;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class InstallServiceTests
{
    private static string MakeZip(string directory, string name, params (string Entry, string Content)[] files)
    {
        var path = Path.Combine(directory, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            var archiveEntry = zip.CreateEntry(entry);
            using var stream = archiveEntry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task Archive_that_already_uses_game_folder_layout_installs_as_is()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "pack.zip", ("mods/bikes/Thing.pkz", "bike"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.False(plan.NeedsTypeChoice);
        Assert.Equal("bikes", plan.Type!.Key);
        Assert.Single(plan.Files);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message + " errors: " + string.Join("; ", result.Errors));
        Assert.Equal("bike", File.ReadAllText(Path.Combine(env.ModsRoot, "bikes", "Thing.pkz")));
    }

    [Fact]
    public async Task Loose_track_files_are_detected_and_wrapped_in_a_folder()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "SweetTrack.zip",
            ("SweetTrack.map", "map-data"),
            ("textures/grass.dds", "tex"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.False(plan.NeedsTypeChoice);
        Assert.Equal("tracks", plan.Type!.Key);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message + " errors: " + string.Join("; ", result.Errors) + " files: " + string.Join(", ", plan.Files.Select(f => f.EntryKey + " -> " + f.DestinationPath)));
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "tracks", "Mods", "SweetTrack", "SweetTrack.map")));
    }

    [Fact]
    public async Task Existing_files_are_backed_up_before_being_replaced()
    {
        using var env = new TestEnvironment();
        var existing = env.Touch("bikes/Thing.pkz", "old-bike");
        var zip = MakeZip(env.Root, "pack.zip", ("bikes/Thing.pkz", "new-bike"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);
        Assert.Equal(1, plan.OverwriteCount);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success);
        Assert.Equal(1, result.BackedUp);
        Assert.Equal("new-bike", File.ReadAllText(existing));

        var backups = Directory.GetFiles(env.Config.Data.Backups, "Thing.pkz", SearchOption.AllDirectories);
        Assert.Single(backups);
        Assert.Equal("old-bike", File.ReadAllText(backups[0]));
    }

    [Fact]
    public async Task Bare_pkz_goes_into_the_chosen_type_folder()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Dune_Bike.pkz", "payload");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pkz, preferredTypeKey: "bikes");
        Assert.False(plan.NeedsTypeChoice);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Dune_Bike.pkz")));
    }

    [Fact]
    public void Unrecognised_archive_asks_for_a_type()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "Mystery.zip", ("thing.dat", "x"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.True(plan.NeedsTypeChoice);
        Assert.Null(plan.Type);
        Assert.NotEmpty(plan.Entries);

        installer.Finalize(plan, env.Type("misc"));
        Assert.True(plan.Ready);
        Assert.All(plan.Files, f => Assert.StartsWith(
            Path.Combine(env.ModsRoot, "misc"), f.DestinationPath, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("bikes/../../evil.txt")]
    public void Path_traversal_entries_are_rejected(string entry)
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "Evil.zip", (entry, "x"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);
        installer.Finalize(plan, env.Type("bikes"));

        Assert.Empty(plan.Files);
        Assert.Contains(plan.Warnings, w => w.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "evil.txt")));
    }

    [Fact]
    public async Task Folder_drops_are_copied_under_the_chosen_type()
    {
        using var env = new TestEnvironment();
        var source = Path.Combine(env.Root, "Downloaded Mod");
        Directory.CreateDirectory(Path.Combine(source, "paints"));
        File.WriteAllText(Path.Combine(source, "paints", "a.pnt"), "paint");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(source, preferredTypeKey: "bikes");

        Assert.True(plan.Ready);
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Downloaded Mod", "paints", "a.pnt")));
    }
}
