using MXBikesModManager.Services;

namespace MXBikesModManager;

/// <summary>Headless mode used for smoke tests and troubleshooting.</summary>
public static class CliRunner
{
    public static int RunScan(CliOptions options)
    {
        if (options.Mode == CliMode.Help)
        {
            Console.WriteLine(CliOptions.Usage);
            return 0;
        }

        var config = ConfigService.Load(options.ConfigPath);
        if (!string.IsNullOrWhiteSpace(options.ModsRoot))
        {
            config.Config.Game.ModsRoot = options.ModsRoot!;
        }

        Console.WriteLine($"Config:    {config.ConfigPath}");
        Console.WriteLine($"Data:      {config.Data.Root}");
        Console.WriteLine($"Mods root: {config.Config.Game.ModsRoot}");

        if (!Directory.Exists(config.Config.Game.ModsRoot))
        {
            Console.Error.WriteLine("Mods folder does not exist.");
            return 2;
        }

        var cache = new MetadataCache(config.Data.Cache, config.Log);
        var scanner = new ModScanner(config, cache);
        var started = DateTime.UtcNow;
        var failures = 0;

        Console.WriteLine();
        Console.WriteLine($"{"Type",-10} {"Total",6} {"Enabled",8} {"Disabled",9} {"Preview",8} {"Size",10}  Problems");
        Console.WriteLine(new string('-', 78));

        long totalSize = 0;
        var totalMods = 0;

        foreach (var type in config.Config.ModTypes)
        {
            var result = scanner.ScanTypeAsync(type, CancellationToken.None).GetAwaiter().GetResult();
            var enabled = result.Entries.Count(e => e.IsEnabled);
            var disabled = result.Entries.Count - enabled;
            var size = result.Entries.Sum(e => e.SizeBytes);
            totalSize += size;
            totalMods += result.Entries.Count;
            failures += result.Problems.Count;

            Console.WriteLine(
                $"{type.DisplayTitle,-10} {result.Entries.Count,6} {enabled,8} {disabled,9} {result.WithPreview,8} " +
                $"{PathUtil.FormatSize(size),10}  {(result.Problems.Count > 0 ? result.Problems[0] : string.Empty)}");

            var categories = result.Entries
                .Where(e => !string.IsNullOrEmpty(e.Category))
                .GroupBy(e => e.Category)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (categories.Count > 0)
            {
                Console.WriteLine("    categories: " +
                    string.Join(" · ", categories.Select(g => $"{g.Key} {g.Count()}")));
            }

            foreach (var entry in result.Entries.Take(3))
            {
                var name = entry.DisplayName;
                var preview = entry.Preview is null ? "" : " [preview]";
                Console.WriteLine($"    · {name}{preview}  ({PathUtil.FormatSize(entry.SizeBytes)})");
            }
        }

        cache.SaveIfDirty();
        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{totalMods} mods, {PathUtil.FormatSize(totalSize)} total, scanned in {elapsed:0.00}s");
        Console.WriteLine($"Log: {Path.Combine(config.Data.Logs, "app.log")}");

        return failures == 0 ? 0 : 1;
    }
}
