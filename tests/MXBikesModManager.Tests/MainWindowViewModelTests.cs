using MXBikesModManager.Services;
using MXBikesModManager.ViewModels;
using Xunit;

namespace MXBikesModManager.Tests;

public class MainWindowViewModelTests
{
    private sealed class FakeUi : IUiInteraction
    {
        public InstallPlan? Plan { get; private set; }
        public IReadOnlyList<string> PaintBikes { get; private set; } = Array.Empty<string>();
        public InstallChoice? Choice { get; init; }
        public List<string> Messages { get; } = new();

        public Task<InstallChoice?> PickInstallTypeAsync(InstallPlan plan, IReadOnlyList<string> paintBikes)
        {
            Plan = plan;
            PaintBikes = paintBikes;
            return Task.FromResult(Choice);
        }

        public Task<IReadOnlyList<string>> PickModArchivesAsync() =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue") =>
            Task.FromResult(true);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task<bool> ShowSettingsAsync(ConfigService config) => Task.FromResult(false);
    }

    private sealed class FakeTrashService : ITrashService
    {
        public List<string> Paths { get; } = new();

        public Task<TrashResult> TrashAsync(string path)
        {
            Paths.Add(path);
            return Task.FromResult(new TrashResult(true, "Moved to Trash."));
        }
    }

    [Fact]
    public async Task Successful_install_moves_the_source_to_trash_when_checked()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Fox Instinct.pkz", "boot-payload");
        var trash = new FakeTrashService();

        var ui = new FakeUi
        {
            Choice = new InstallChoice(env.Type("rider"), "boots", TrashSource: true)
        };
        var vm = new MainWindowViewModel(env.Config, ui, "rider", trash);
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pkz });

        Assert.Equal(new[] { pkz }, trash.Paths);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "rider", "boots", "Fox Instinct.pkz")));
    }

    [Fact]
    public async Task Unchecked_install_keeps_the_source()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Fox Instinct.pkz", "boot-payload");
        var trash = new FakeTrashService();

        var ui = new FakeUi
        {
            Choice = new InstallChoice(env.Type("rider"), "boots", TrashSource: false)
        };
        var vm = new MainWindowViewModel(env.Config, ui, "rider", trash);
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pkz });

        Assert.Empty(trash.Paths);
        Assert.True(File.Exists(pkz));
    }

    [Fact]
    public async Task Cancelling_the_dialog_never_trashes_the_source()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Fox Instinct.pkz", "boot-payload");
        var trash = new FakeTrashService();

        var ui = new FakeUi { Choice = null };
        var vm = new MainWindowViewModel(env.Config, ui, "rider", trash);
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pkz });

        Assert.Empty(trash.Paths);
        Assert.True(File.Exists(pkz));
    }

    [Fact]
    public async Task Dropping_a_paint_asks_for_a_bike_and_installs_into_its_paints_folder()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/AK KTM SXF 250F.pkz", "pkz");
        env.MakeDirectory("bikes/AK KTM SXF 250F/paints");
        var pnt = env.Touch("downloads/Blue Flame.pnt", "paint-data");

        var ui = new FakeUi
        {
            Choice = new InstallChoice(env.Type("bikes"), null, "AK KTM SXF 250F")
        };
        var vm = new MainWindowViewModel(env.Config, ui, "bikes");
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pnt });

        Assert.NotNull(ui.Plan);
        Assert.True(ui.Plan!.IsBikePaint);
        Assert.Contains("AK KTM SXF 250F", ui.PaintBikes);
        Assert.True(File.Exists(Path.Combine(
            env.ModsRoot, "bikes", "AK KTM SXF 250F", "paints", "Blue Flame.pnt")));
    }

    [Fact]
    public async Task Cancelling_the_paint_dialog_does_not_install_anything()
    {
        using var env = new TestEnvironment();
        env.MakeDirectory("bikes/AK KTM SXF 250F/paints");
        var pnt = env.Touch("downloads/Blue Flame.pnt", "paint-data");

        var ui = new FakeUi { Choice = null };
        var vm = new MainWindowViewModel(env.Config, ui, "bikes");
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pnt });

        Assert.DoesNotContain(
            Directory.GetFiles(env.ModsRoot, "*.pnt", SearchOption.AllDirectories),
            file => file.Contains("AK KTM SXF 250F", StringComparison.Ordinal));
        Assert.NotNull(ui.Plan);
        Assert.True(ui.Plan!.IsBikePaint);
    }

    [Fact]
    public async Task Choosing_another_type_instead_of_paint_still_uses_the_normal_installer()
    {
        using var env = new TestEnvironment();
        var pnt = env.Touch("downloads/paint.pnt", "paint-data");

        var ui = new FakeUi { Choice = new InstallChoice(env.Type("misc"), null) };
        var vm = new MainWindowViewModel(env.Config, ui, "bikes");
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pnt });

        Assert.NotNull(ui.Plan);
        Assert.Equal("misc", ui.Plan!.Type!.Key);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "misc", "paint.pnt")));
    }

    [Fact]
    public async Task Boots_install_into_rider_boots_when_chosen_in_the_dialog()
    {
        using var env = new TestEnvironment();
        var pkz = env.Touch("downloads/Fox Instinct 2.0 by Aeffertz.pkz", "boot-payload");

        var ui = new FakeUi
        {
            Choice = new InstallChoice(env.Type("rider"), "boots")
        };
        // The Bikes tab is active, but the file name says boots, so Rider is suggested.
        var vm = new MainWindowViewModel(env.Config, ui, "bikes");
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { pkz });

        Assert.NotNull(ui.Plan);
        Assert.Equal("rider", ui.Plan!.DetectedTypeKey);
        Assert.True(File.Exists(Path.Combine(
            env.ModsRoot, "rider", "boots", "Fox Instinct 2.0 by Aeffertz.pkz")));
        Assert.False(File.Exists(Path.Combine(
            env.ModsRoot, "rider", "Fox Instinct 2.0 by Aeffertz.pkz")));
    }

    [Fact]
    public async Task Bulk_disable_and_enable_sync_the_tile_state_and_summary()
    {
        using var env = new TestEnvironment();
        env.Touch("bikes/Bulk_A.pkz", "pkz");
        env.Touch("bikes/Bulk_B.pkz", "pkz");

        var vm = new MainWindowViewModel(env.Config, new FakeUi(), "bikes");
        vm.Initialize();
        await vm.RefreshAsync();

        var typeVm = vm.Types.Single(t => t.Type.Key == "bikes");
        Assert.Equal(2, typeVm.AllItems.Count);
        foreach (var item in typeVm.AllItems) item.IsSelected = true;

        await vm.DisableSelectedCommand.ExecuteAsync(null);

        Assert.All(typeVm.AllItems, item =>
        {
            Assert.False(item.IsEnabled);
            Assert.False(item.Entry.IsEnabled);
        });
        Assert.Contains("0 enabled", vm.Summary, StringComparison.Ordinal);

        await vm.EnableSelectedCommand.ExecuteAsync(null);

        Assert.All(typeVm.AllItems, item =>
        {
            Assert.True(item.IsEnabled);
            Assert.True(item.Entry.IsEnabled);
        });
        Assert.Contains("2 enabled", vm.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_removal_keeps_the_item_in_the_grid()
    {
        using var env = new TestEnvironment();
        var path = env.Touch("bikes/Gone.pkz", "payload");

        var vm = new MainWindowViewModel(env.Config, new FakeUi(), "bikes");
        vm.Initialize();
        await vm.RefreshAsync();

        var typeVm = vm.Types.Single(t => t.Type.Key == "bikes");
        var item = typeVm.AllItems.Single(i => i.Entry.BaseName == "Gone");
        item.IsSelected = true;
        File.Delete(path);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Contains(item, typeVm.AllItems);
        Assert.Contains("failed", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Already_ready_layout_archives_still_show_the_confirmation_dialog()
    {
        using var env = new TestEnvironment();
        var zip = Path.Combine(env.Root, "pack.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("bikes/Thing.pkz").Open();
            using var writer = new StreamWriter(stream);
            writer.Write("bike");
        }

        var ui = new FakeUi { Choice = new InstallChoice(env.Type("bikes"), null) };
        var vm = new MainWindowViewModel(env.Config, ui, "bikes");
        vm.Initialize();

        await vm.InstallPathsAsync(new[] { zip });

        Assert.NotNull(ui.Plan);
        Assert.True(ui.Plan!.HasGameLayout);
        Assert.True(File.Exists(Path.Combine(env.ModsRoot, "bikes", "Thing.pkz")));
    }
}
