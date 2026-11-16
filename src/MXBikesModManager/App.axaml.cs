using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MXBikesModManager.Services;

namespace MXBikesModManager;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var cli = new CliOptions(desktop.Args ?? Array.Empty<string>());
            var config = ConfigService.Load(cli.ConfigPath);

            // Install the palette before any window is created so every DynamicResource resolves.
            ThemeService.Apply(cli.Theme ?? config.Config.Ui.Theme);

            if (!string.IsNullOrWhiteSpace(cli.ModsRoot))
            {
                // Runtime override only: don't persist a temporary path over the user's config.
                config.Config.Game.ModsRoot = cli.ModsRoot!;
                config.DetectionNote = $"Using mods folder from command line: {cli.ModsRoot}";
            }

            desktop.MainWindow = new Views.MainWindow(config, cli.Tab);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
