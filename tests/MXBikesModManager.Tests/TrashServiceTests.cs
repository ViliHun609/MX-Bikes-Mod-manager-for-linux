using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class TrashServiceTests
{
    [Fact]
    public void A_normal_download_is_allowed()
    {
        using var env = new TestEnvironment();
        using var temp = new TestDir();
        var file = Path.Combine(temp.Path, "mod.pkz");
        File.WriteAllText(file, "x");

        Assert.True(SystemTrashService.CanTrash(
            file, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out var reason), reason);
    }

    [Fact]
    public void Anything_inside_the_mods_folder_is_refused()
    {
        using var env = new TestEnvironment();
        var file = env.Touch("rider/mod.pkz");

        Assert.False(SystemTrashService.CanTrash(
            file, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
        Assert.False(SystemTrashService.CanTrash(
            env.ModsRoot, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
    }

    [Fact]
    public void App_data_and_config_are_refused()
    {
        using var env = new TestEnvironment();
        var dataFile = Path.Combine(env.Config.Data.Root, "cache.bin");
        File.WriteAllText(dataFile, "x");

        Assert.False(SystemTrashService.CanTrash(
            dataFile, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
        Assert.False(SystemTrashService.CanTrash(
            env.Config.ConfigPath, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
    }

    [Fact]
    public void A_folder_that_contains_the_mods_tree_is_refused()
    {
        using var env = new TestEnvironment();

        Assert.False(SystemTrashService.CanTrash(
            env.Root, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
    }

    [Fact]
    public void The_home_directory_is_refused()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return;

        using var env = new TestEnvironment();
        Assert.False(SystemTrashService.CanTrash(
            home, env.ModsRoot, env.Config.Data.Root, env.Config.ConfigPath, out _));
    }

    [Fact]
    public void Trashing_the_source_is_on_by_default()
    {
        var config = ConfigService.CreateDefault();
        Assert.True(config.Ui.TrashSourceAfterInstall);
    }
}
