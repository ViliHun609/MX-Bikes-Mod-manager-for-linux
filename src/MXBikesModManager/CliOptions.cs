namespace MXBikesModManager;

public enum CliMode
{
    Gui,
    Scan,
    Help
}

public sealed class CliOptions
{
    public string? ConfigPath { get; }
    public string? ModsRoot { get; }
    public string? Tab { get; }
    public CliMode Mode { get; }

    public CliOptions(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config" or "-c":
                    ConfigPath = Next(args, ref i);
                    break;
                case "--mods" or "-m":
                    ModsRoot = Next(args, ref i);
                    break;
                case "--tab" or "-t":
                    Tab = Next(args, ref i);
                    break;
                case "--scan" or "-s":
                    Mode = CliMode.Scan;
                    break;
                case "--help" or "-h":
                    Mode = CliMode.Help;
                    break;
            }
        }
    }

    private static string? Next(string[] args, ref int index)
        => index + 1 < args.Length ? args[++index] : null;

    public static string Usage =>
        """
        MX Bikes Mod Manager

        Usage:
          MXBikesModManager [--config <appsettings.json>] [--mods <mods folder>] [--tab <key>]
          MXBikesModManager --scan [--config <path>] [--mods <mods folder>]

        Options:
          -c, --config   Path to appsettings.json (default: next to the app, then the repo root)
          -m, --mods     Override the mods folder for this run
          -t, --tab      Open on this mod type (bikes, tracks, tyres, rider, misc)
          -s, --scan     Scan the mods folder, print a summary and exit (no window)
          -h, --help     Show this help
        """;
}
