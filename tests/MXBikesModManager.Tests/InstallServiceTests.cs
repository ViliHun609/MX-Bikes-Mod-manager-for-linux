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
    public async Task Bare_pkz_asks_for_a_type_but_suggests_the_current_tab()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Dune_Bike.pkz", "payload");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pkz, preferredTypeKey: "bikes");

        // PiBoSo .pkz files are obfuscated, so the type must always be confirmed.
        Assert.True(plan.NeedsTypeChoice);
        Assert.Null(plan.Type);
        Assert.Equal("bikes", plan.DetectedTypeKey);

        installer.Finalize(plan, env.Type(plan.DetectedTypeKey!));
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

        Assert.True(plan.NeedsTypeChoice);
        installer.Finalize(plan, env.Type("bikes"));
        Assert.True(plan.Ready);
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Downloaded Mod", "paints", "a.pnt")));
    }

    [Fact]
    public async Task Bare_pnt_is_detected_as_a_bike_paint_and_targets_the_chosen_bike()
    {
        using var env = new TestEnvironment();
        env.MakeDirectory("bikes/AK KTM SXF 250F/paints");
        var pnt = env.Touch("downloads/Blue Flame.pnt", "paint-data");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pnt);

        Assert.True(plan.IsBikePaint);
        Assert.True(plan.NeedsTypeChoice);
        Assert.Null(plan.Type);
        Assert.Contains("AK KTM SXF 250F", installer.GetPaintBikes());

        Assert.True(installer.FinalizePaint(plan, "AK KTM SXF 250F"));
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        var installed = Path.Combine(env.ModsRoot, "bikes", "AK KTM SXF 250F", "paints", "Blue Flame.pnt");
        Assert.True(File.Exists(installed));
        Assert.Equal("paint-data", File.ReadAllText(installed));
    }

    [Fact]
    public async Task Paint_archive_lands_flat_in_the_bike_paints_folder()
    {
        using var env = new TestEnvironment();
        env.MakeDirectory("bikes/AK Honda CRF 450F/paints");
        var zip = MakeZip(env.Root, "Team Graphics.zip",
            ("paints/blue.pnt", "blue"),
            ("paints/red.pnt", "red"),
            ("readme.txt", "notes"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);
        Assert.True(plan.IsBikePaint);
        Assert.True(plan.NeedsTypeChoice);

        Assert.True(installer.FinalizePaint(plan, "AK Honda CRF 450F"));
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        var paints = Path.Combine(env.ModsRoot, "bikes", "AK Honda CRF 450F", "paints");
        Assert.Equal("blue", File.ReadAllText(Path.Combine(paints, "blue.pnt")));
        Assert.Equal("red", File.ReadAllText(Path.Combine(paints, "red.pnt")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(paints, "readme.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(env.ModsRoot, "bikes"), "Team Graphics"));
    }

    [Fact]
    public async Task Paint_archive_using_the_full_game_layout_still_asks_for_a_bike()
    {
        using var env = new TestEnvironment();
        env.MakeDirectory("bikes/AK Yamaha YZF 450F/paints");
        var zip = MakeZip(env.Root, "layout.zip",
            ("bikes/AK Yamaha YZF 450F/paints/one.pnt", "one"),
            ("bikes/AK Yamaha YZF 450F/paints/two.pnt", "two"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);

        Assert.True(plan.IsBikePaint);
        Assert.True(plan.NeedsTypeChoice);
        Assert.Null(plan.Type);

        Assert.True(installer.FinalizePaint(plan, "AK Yamaha YZF 450F"));
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(
            env.ModsRoot, "bikes", "AK Yamaha YZF 450F", "paints", "one.pnt")));
    }

    [Fact]
    public async Task Replacing_a_paint_backs_up_the_old_file()
    {
        using var env = new TestEnvironment();
        env.MakeDirectory("bikes/AK KTM SXF 250F/paints");
        env.Touch("bikes/AK KTM SXF 250F/paints/blue.pnt", "old-paint");
        var pnt = env.Touch("downloads/blue.pnt", "new-paint");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pnt);
        Assert.True(installer.FinalizePaint(plan, "AK KTM SXF 250F"));
        Assert.Equal(1, plan.OverwriteCount);

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success);
        Assert.Equal(1, result.BackedUp);
        Assert.Equal("new-paint", File.ReadAllText(
            Path.Combine(env.ModsRoot, "bikes", "AK KTM SXF 250F", "paints", "blue.pnt")));
        var backups = Directory.GetFiles(env.Config.Data.Backups, "blue.pnt", SearchOption.AllDirectories);
        Assert.Single(backups);
        Assert.Equal("old-paint", File.ReadAllText(backups[0]));
    }

    [Fact]
    public async Task Paint_for_a_disabled_bike_goes_into_the_disabled_folder()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/AK KTM SXF 250F.pkz.disabled", "pkz");
        env.MakeDirectory("bikes/AK KTM SXF 250F.disabled/paints");
        var pnt = env.Touch("downloads/blue.pnt", "paint");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pnt);
        Assert.Contains("AK KTM SXF 250F", installer.GetPaintBikes());

        Assert.True(installer.FinalizePaint(plan, "AK KTM SXF 250F"));
        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(
            env.ModsRoot, "bikes", "AK KTM SXF 250F.disabled", "paints", "blue.pnt")));
        Assert.False(Directory.Exists(Path.Combine(env.ModsRoot, "bikes", "AK KTM SXF 250F")));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("nested/bike")]
    public void Paint_target_rejects_bike_names_that_are_paths(string bike)
    {
        using var env = new TestEnvironment();
        var pnt = env.Touch("downloads/blue.pnt", "paint");

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(pnt);

        Assert.False(installer.FinalizePaint(plan, bike));
        Assert.Empty(plan.Files);
    }

    [Fact]
    public void Suggests_a_bike_when_its_name_is_in_the_file_name()
    {
        var bikes = new[] { "AK KTM SXF 250F", "AK Honda CRF 450F", "MX1OEM_2023_KTM_450_SX-F" };

        Assert.Equal("AK KTM SXF 250F",
            InstallService.SuggestPaintBike("/tmp/AK KTM SXF 250F - blue graphics.zip", bikes));
        Assert.Equal("MX1OEM_2023_KTM_450_SX-F",
            InstallService.SuggestPaintBike("/tmp/MX1OEM_2023_KTM_450_SX-F_paint.zip", bikes));
        Assert.Null(InstallService.SuggestPaintBike("/tmp/random graphics.zip", bikes));
    }

    [Theory]
    [InlineData("Fox Instinct 2.0 by Aeffertz.pkz", "rider")]
    [InlineData("Dunlop MX33 tyre.zip", "tyres")]
    [InlineData("Red Bull Supercross track.zip", "tracks")]
    [InlineData("KTM SXF 450 graphics.zip", "bike-paint")]
    [InlineData("Honda CRF 450.pkz", "bikes")]
    public void Suggests_the_mod_type_from_the_file_name(string fileName, string expected)
    {
        using var env = new TestEnvironment();
        var installer = new InstallService(env.Config);

        var suggested = installer.SuggestTypeKey(Path.Combine("/tmp", fileName), fallback: "misc");

        Assert.Equal(expected, suggested);
    }

    [Theory]
    [InlineData("Fox Instinct 2.0 by Aeffertz.pkz", "boots")]
    [InlineData("Fox V3 Helmet.zip", "helmets")]
    [InlineData("Leatt Chest Protector.zip", "protections")]
    [InlineData("Aeffertz rider gear.zip", "riders")]
    [InlineData("GoPro helmet cam.zip", "helmetcams")]
    public void Suggests_rider_subfolders_from_the_file_name(string fileName, string expected)
    {
        using var env = new TestEnvironment();
        var source = Path.Combine("/tmp", fileName);

        Assert.Equal(expected, InstallService.SuggestSubfolder(source, env.Type("rider")));
    }

    [Fact]
    public async Task Full_layout_archive_can_be_retargeted_to_another_type()
    {
        using var env = new TestEnvironment();
        var zip = MakeZip(env.Root, "layout.zip", ("bikes/Thing.pkz", "bike"));

        var installer = new InstallService(env.Config);
        var plan = installer.Analyze(zip);
        Assert.True(plan.HasGameLayout);
        Assert.Equal("bikes", plan.Type!.Key);

        // The dialog lets the user correct the detected type.
        installer.Finalize(plan, env.Type("misc"));

        var result = await installer.InstallAsync(plan);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "misc", "Thing.pkz")));
        Assert.False(File.Exists(Path.Combine(env.ModsRoot, "misc", "bikes", "Thing.pkz")));
    }
}
