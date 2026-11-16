using Avalonia;

namespace MXBikesModManager;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless diagnostics: print a scan summary and exit. Used for smoke tests / CI.
        var cli = new CliOptions(args);
        if (cli.Mode is CliMode.Scan or CliMode.Help)
        {
            return CliRunner.RunScan(cli);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // The X11 backend runs through XWayland on Wayland desktops, and Wayland→XWayland
        // file drops are unreliable (notably on Hyprland). Use the native Wayland backend
        // when a compositor is available so drag & drop works from native file managers.
        // MXB_FORCE_X11=1 is the escape hatch if a compositor misbehaves.
        var forceX11 = Environment.GetEnvironmentVariable("MXB_FORCE_X11") is "1" or "true";
        if (!forceX11 &&
            OperatingSystem.IsLinux() &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            builder = builder.UseWayland();
        }

        return builder;
    }
}
