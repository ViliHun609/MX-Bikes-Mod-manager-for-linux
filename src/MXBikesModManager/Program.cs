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
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
