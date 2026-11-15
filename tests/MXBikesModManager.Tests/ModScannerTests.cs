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
