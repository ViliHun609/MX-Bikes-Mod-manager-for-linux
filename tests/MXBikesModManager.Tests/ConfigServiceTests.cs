using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Explicit_config_path_is_created_when_missing()
    {
        using var temp = new TestDir();
        var configPath = Path.Combine(temp.Path, "nested", "appsettings.json");

        var config = ConfigService.Load(configPath);

        Assert.Equal(Path.GetFullPath(configPath), config.ConfigPath);
        Assert.True(File.Exists(configPath));
        Assert.StartsWith(temp.Path, config.Data.Root, StringComparison.Ordinal);
        Assert.True(Directory.Exists(config.Data.Logs));
        Assert.True(Directory.Exists(config.Data.Backups));
        Assert.True(Directory.Exists(config.Data.Trash));
    }

    [Fact]
    public void Data_paths_stay_next_to_the_config_file()
    {
        using var temp = new TestDir();
        var configPath = Path.Combine(temp.Path, "appsettings.json");

        var config = ConfigService.Load(configPath);

        Assert.Equal(Path.Combine(temp.Path, "data"), config.Data.Root);
        Assert.Equal(Path.Combine(temp.Path, "data", "trash"), config.Data.Trash);
    }

    [Fact]
    public void Saved_config_omits_computed_properties_and_removed_keys()
    {
        using var temp = new TestDir();
        var configPath = Path.Combine(temp.Path, "appsettings.json");
        var config = ConfigService.Load(configPath);
        config.Config.Game.AutoDetect = false;

        Assert.True(config.Save());

        var json = File.ReadAllText(configPath);
        Assert.DoesNotContain("displayTitle", json);
        Assert.DoesNotContain("subfoldersToScan", json);
        Assert.DoesNotContain("iconGeometry", json);
        Assert.DoesNotContain("scanDepth", json);
        Assert.DoesNotContain("disableStrategy", json);
        Assert.DoesNotContain("\"layout\"", json);
        Assert.DoesNotContain("tileWidth", json);
        Assert.DoesNotContain("rememberWindow", json);
        Assert.EndsWith(Environment.NewLine, json);
    }
}
