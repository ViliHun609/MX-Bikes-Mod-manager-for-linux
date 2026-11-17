using System.IO.Compression;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class ModScannerTests
{
    private static (ModScanner Scanner, MetadataCache Cache) Create(TestEnvironment env)
    {
        var cache = new MetadataCache(env.Config.Data.Cache, env.Config.Log);
        return (new ModScanner(env.Config, cache), cache);
    }

    [Fact]
    public async Task Pairs_pkz_with_its_asset_folder_and_counts_it_once()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Husky_TC85.pkz");
        env.Touch("bikes/Husky_TC85/paints/-stock-.pnt");
        env.Touch("bikes/Solo_CRF.pkz");
        env.MakeDirectory("bikes/Loose_Mod");
        env.Touch("bikes/Loose_Mod/bike.cfg");
        env.Touch("bikes/Disabled_Bike.pkz.disabled");

        var (scanner, _) = Create(env);
        var result = await scanner.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);

        Assert.Equal(4, result.Entries.Count);
        Assert.Equal(3, result.Entries.Count(e => e.IsEnabled));

        var husky = result.Entries.Single(e => e.BaseName == "Husky_TC85");
        Assert.NotNull(husky.PairedFolderPath);
        Assert.True(husky.IsEnabled);

        var solo = result.Entries.Single(e => e.BaseName == "Solo_CRF");
        Assert.Null(solo.PairedFolderPath);
        Assert.Equal(Models.ModKind.PkzArchive, solo.Kind);

        var loose = result.Entries.Single(e => e.BaseName == "Loose_Mod");
        Assert.Equal(Models.ModKind.Folder, loose.Kind);

        var disabled = result.Entries.Single(e => e.BaseName == "Disabled_Bike");
        Assert.False(disabled.IsEnabled);
        Assert.EndsWith(".disabled", disabled.Path);
    }

    [Fact]
    public async Task Scans_track_subfolders_and_rider_categories()
    {
        using var env = new TestEnvironment();
        env.Touch("tracks/Mods/Track_One.pkz");
        env.Touch("tracks/supercross/Track_Two.pkz");
        env.Touch("rider/helmets/Helmet_A.pkz");
        env.Touch("rider/riders/Rider_B.pkz");

        var (scanner, _) = Create(env);

        var tracks = await scanner.ScanTypeAsync(env.Type("tracks"), CancellationToken.None);
        Assert.Equal(2, tracks.Entries.Count);

        var rider = await scanner.ScanTypeAsync(env.Type("rider"), CancellationToken.None);
        Assert.Equal(2, rider.Entries.Count);
        Assert.Contains(rider.Entries, e => e.Category == "Helmets");
        Assert.Contains(rider.Entries, e => e.Category == "Riders");
    }

    [Fact]
    public async Task Skips_hidden_folders_and_missing_type_folders()
    {
        using var env = new TestEnvironment();
        Directory.Delete(Path.Combine(env.ModsRoot, "misc"), recursive: true);
        env.MakeDirectory("bikes/.hidden");
        env.Touch("bikes/.hidden/foo.pkz");

        var (scanner, _) = Create(env);

        var bikes = await scanner.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        Assert.Empty(bikes.Entries);

        var misc = await scanner.ScanTypeAsync(env.Type("misc"), CancellationToken.None);
        Assert.Single(misc.Problems);
    }

    [Fact]
    public async Task Name_rules_categorise_mods_and_keep_folder_pairing()
    {
        using var env = new TestEnvironment(config =>
        {
            var bikes = config.ModTypes.First(t => t.Key == "bikes");
            bikes.DefaultCategory = "Other";
            bikes.CategoryRules.Clear();
            bikes.CategoryRules.Add(new Models.CategoryRule { Name = "MX1", Pattern = "^MX1OEM" });
            bikes.CategoryRules.Add(new Models.CategoryRule { Name = "85cc", Pattern = "^85(cc)?[ _-]" });
        });

        env.Touch("bikes/MX1OEM_2023_KTM_450_SX-F.pkz");
        env.Touch("bikes/MX1OEM_2023_KTM_450_SX-F/paints/a.pnt");
        env.Touch("bikes/85CC_ALPHAMODS_KTM_SX2022.pkz");
        env.Touch("bikes/Unknown_Thing.pkz");

        var (scanner, _) = Create(env);
        var result = await scanner.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);

        Assert.Equal(3, result.Entries.Count);

        var mx1 = result.Entries.Single(e => e.BaseName.StartsWith("MX1OEM"));
        Assert.Equal("MX1", mx1.Category);
        Assert.NotNull(mx1.PairedFolderPath);

        // Rule matching is case-insensitive.
        Assert.Equal("85cc", result.Entries.Single(e => e.BaseName.StartsWith("85CC")).Category);
        Assert.Equal("Other", result.Entries.Single(e => e.BaseName == "Unknown_Thing").Category);
    }

    [Fact]
    public async Task Name_rules_do_not_override_subfolder_categories()
    {
        using var env = new TestEnvironment(config =>
        {
            var rider = config.ModTypes.First(t => t.Key == "rider");
            rider.CategoryRules.Add(new Models.CategoryRule { Name = "Everything", Pattern = ".*" });
        });

        env.Touch("rider/helmets/Helmet_A.pkz");
        env.Touch("rider/riders/Rider_B.pkz");

        var (scanner, _) = Create(env);
        var result = await scanner.ScanTypeAsync(env.Type("rider"), CancellationToken.None);

        // Even though the catch-all rule matches, the scan subfolder category wins.
        Assert.Equal("Helmets", result.Entries.Single(e => e.BaseName == "Helmet_A").Category);
        Assert.Equal("Riders", result.Entries.Single(e => e.BaseName == "Rider_B").Category);
    }

    [Fact]
    public async Task Cached_preview_follows_the_archive_when_the_mod_is_toggled()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.ModsRoot, "bikes", "Preview_Bike.pkz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var ini = new StreamWriter(zip.CreateEntry("bike/bike.ini").Open()))
            {
                ini.Write("[info]\nname = Preview Bike\n");
            }
            using var image = zip.CreateEntry("bike/capa.png").Open();
            image.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        }

        var (scanner, cache) = Create(env);
        var first = await scanner.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        var entry = first.Entries.Single(e => e.BaseName == "Preview_Bike");
        Assert.NotNull(entry.Preview);
        Assert.EndsWith("Preview_Bike.pkz", entry.Preview!.ArchivePath);
        cache.SaveIfDirty();

        Assert.True(new ToggleService(env.Config).SetEnabled(entry, false).Success);

        // A fresh scan (cache reloaded from disk) must not point at the old, missing path.
        var (scanner2, _) = Create(env);
        var second = await scanner2.ScanTypeAsync(env.Type("bikes"), CancellationToken.None);
        var disabled = second.Entries.Single(e => e.BaseName == "Preview_Bike");

        Assert.False(disabled.IsEnabled);
        Assert.NotNull(disabled.Preview);
        Assert.EndsWith("Preview_Bike.pkz.disabled", disabled.Preview!.ArchivePath);
        Assert.True(File.Exists(disabled.Preview.ArchivePath));
    }

    [Theory]
    [InlineData("Bike.pkz", "Bike", false)]
    [InlineData("Bike.pkz.disabled", "Bike", true)]
    [InlineData("Bike.PKZ", "Bike", false)]
    [InlineData("Bike.zip", "", false)]
    public void Parses_archive_names(string fileName, string expectedBase, bool expectedDisabled)
    {
        var ok = ModScanner.TryParseArchiveName(fileName, ".disabled", out var baseName, out var disabled);
        Assert.Equal(expectedBase.Length > 0, ok);
        Assert.Equal(expectedBase, baseName);
        Assert.Equal(expectedDisabled, disabled);
    }
}
