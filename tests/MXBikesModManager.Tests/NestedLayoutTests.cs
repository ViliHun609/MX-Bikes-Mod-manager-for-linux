using System.IO.Compression;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class NestedLayoutTests
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

    private static string MakeFolder(string root, string relative, params (string File, string Content)[] files)
    {
        var folder = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        foreach (var (file, content) in files)
        {
            var path = Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return folder;
    }

    [Theory]
    [InlineData("mods/rider/riders/Fox/Fox.pkz")]
    [InlineData("MX Bikes/mods/rider/riders/Fox/Fox.pkz")]
    [InlineData("DownloadPack/mods/rider/riders/Fox/Fox.pkz")]
    [InlineData("rider/riders/Fox/Fox.pkz")]
    public async Task Nested_packaging_is_normalised_to_rider_riders(string entry)
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "fox.zip", (entry, "boot"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.True(plan.HasGameLayout);
        Assert.Equal("rider", plan.Type!.Key);
        Assert.Equal("riders", plan.LayoutSubfolder);
        Assert.Contains("game layout", plan.DetectedKind);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message + " errors: " + string.Join("; ", result.Errors));
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "Fox", "Fox.pkz")));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "riders", "Fox", "Fox.pkz")));
    }

    [Fact]
    public async Task Duplicate_subfolder_segments_are_collapsed()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "fox.zip", ("mods/rider/riders/riders/Fox/Fox.pkz", "boot"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.Equal("riders", plan.LayoutSubfolder);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "Fox", "Fox.pkz")));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "riders", "Fox", "Fox.pkz")));
    }

    [Fact]
    public async Task Retargeting_a_layout_to_another_subfolder_does_not_double_it()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "fox.zip", ("mods/rider/riders/Fox/Fox.pkz", "boot"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        installer.Finalize(plan, env.Type("rider"), "boots");
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "boots", "Fox", "Fox.pkz")));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "rider", "boots", "riders", "Fox", "Fox.pkz")));
    }

    [Fact]
    public async Task Tracks_mods_subfolder_is_detected_and_preserved()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "track.zip", ("mods/tracks/Mods/MyTrack/MyTrack.map", "map"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.Equal("tracks", plan.Type!.Key);
        Assert.Equal("Mods", plan.LayoutSubfolder);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "tracks", "Mods", "MyTrack", "MyTrack.map")));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "tracks", "Mods", "Mods", "MyTrack", "MyTrack.map")));
    }

    [Fact]
    public async Task A_dropped_folder_with_a_nested_layout_is_installed_per_file()
    {
        using var env = new TestEnvironment();
        var folder = MakeFolder(env.Root, "downloads/FoxPack",
            ("mods/rider/riders/Fox/Fox.pkz", "boot"),
            ("mods/rider/riders/Fox/readme.txt", "hello"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(folder);

        Assert.True(plan.HasGameLayout);
        Assert.True(plan.IsLayoutFolder);
        Assert.Equal("rider", plan.Type!.Key);
        Assert.Contains("folder · game layout", plan.DetectedKind);
        Assert.Equal(2, plan.Files.Count);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message + " errors: " + string.Join("; ", result.Errors));
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "Fox", "Fox.pkz")));
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "Fox", "readme.txt")));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_normal_folder_still_copies_as_a_single_mod_folder()
    {
        using var env = new TestEnvironment();
        var folder = MakeFolder(env.Root, "downloads/MyRiderMod", ("MyRiderMod.pkz", "boot"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(folder);

        Assert.False(plan.HasGameLayout);
        Assert.True(plan.IsFolderCopy);
        Assert.True(plan.NeedsTypeChoice);

        installer.Finalize(plan, env.Type("rider"), "riders");
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "riders", "MyRiderMod", "MyRiderMod.pkz")));
    }
}
