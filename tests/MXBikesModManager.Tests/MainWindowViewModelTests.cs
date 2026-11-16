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

        public Task<string?> PickModsFolderAsync() => Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue") =>
            Task.FromResult(true);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task<bool> ShowSettingsAsync(ConfigService config) => Task.FromResult(false);
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
}
