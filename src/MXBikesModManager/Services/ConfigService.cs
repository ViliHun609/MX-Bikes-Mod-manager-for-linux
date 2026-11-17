using System.Text.Json;
using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

public sealed class DataPaths
{
    public required string Root { get; init; }
    public required string Cache { get; init; }
    public required string Thumbs { get; init; }
    public required string Logs { get; init; }
    public required string Backups { get; init; }
    public required string Trash { get; init; }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { Root, Cache, Thumbs, Logs, Backups, Trash })
        {
            Directory.CreateDirectory(dir);
        }
    }
}

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public AppConfig Config { get; private set; } = new();
    public string ConfigPath { get; private set; } = string.Empty;
    public string BaseDirectory { get; private set; } = AppPaths.LaunchDir;
    public DataPaths Data { get; private set; } = null!;
    public LogService Log { get; private set; } = null!;

    /// <summary>Human readable note about how the mods folder was located, shown in the UI.</summary>
    public string? DetectionNote { get; set; }

    public static ConfigService Load(string? explicitConfigPath)
    {
        var service = new ConfigService();
        service.Initialize(explicitConfigPath);
        return service;
    }

    private void Initialize(string? explicitConfigPath)
    {
        var path = AppPaths.FindConfigPath(explicitConfigPath);
        if (path is null)
        {
            var fallbackDir = Directory.Exists(AppPaths.LaunchDir) ? AppPaths.LaunchDir : Directory.GetCurrentDirectory();
            path = Path.Combine(fallbackDir, "appsettings.json");
        }

        // An explicit --config path is honored even when the file does not exist yet.
        if (!File.Exists(path))
        {
            Config = CreateDefault();
            ConfigPath = path;
            TryWrite(path);
        }
        else
        {
            ConfigPath = path;
            try
            {
                var json = File.ReadAllText(path);
                Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();
            }
            catch (Exception)
            {
                // A broken config must not stop the app: fall back to defaults and keep the file untouched.
                Config = CreateDefault();
            }
        }

        BaseDirectory = Path.GetDirectoryName(ConfigPath) is { Length: > 0 } dir ? dir : AppPaths.LaunchDir;
        Data = ResolveData();
        Data.EnsureCreated();
        Log = new LogService(Data.Logs);
        Log.Info($"Config: {ConfigPath}");
        Log.Info($"Data:   {Data.Root}");

        if (Config.ModTypes.Count == 0)
        {
            Config.ModTypes = CreateDefault().ModTypes;
        }

        DetectModsRoot();
    }

    public void DetectModsRoot()
    {
        if (!string.IsNullOrWhiteSpace(Config.Game.ModsRoot) && Directory.Exists(Config.Game.ModsRoot))
        {
            DetectionNote = null;
            return;
        }

        if (!Config.Game.AutoDetect)
        {
            DetectionNote = "Mods folder is not set. Pick it in Settings.";
            return;
        }

        var result = SteamLocator.FindModsRoot(Config.Game.AppId);
        if (result.ModsRoot is not null)
        {
            Config.Game.ModsRoot = result.ModsRoot;
            DetectionNote = $"Detected mods folder in Proton prefix {Config.Game.AppId}.";
            Log.Info($"Detected mods folder: {result.ModsRoot}");
            Save();
            return;
        }

        DetectionNote = result.Problems.Count > 0
            ? string.Join(" ", result.Problems) + " Set the mods folder manually in Settings."
            : "Could not find the MX Bikes mods folder. Set it manually in Settings.";
        Log.Warn(DetectionNote);
    }

    public bool Save()
    {
        Config.Game.ModsRoot = Config.Game.ModsRoot.Trim();
        return TryWrite(ConfigPath);
    }

    private bool TryWrite(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(Config, JsonOptions) + Environment.NewLine);
            return true;
        }
        catch (Exception ex)
        {
            Log?.Error("Could not save config", ex);
            return false;
        }
    }

    private DataPaths ResolveData()
    {
        string Resolve(string value) =>
            Path.IsPathRooted(value)
                ? value
                : Path.GetFullPath(Path.Combine(BaseDirectory, value));

        var root = Resolve(Config.Data.Root);
        string Sub(string name) =>
            Path.IsPathRooted(name) ? name : Path.Combine(root, name);

        return new DataPaths
        {
            Root = root,
            Cache = Sub(Config.Data.Cache),
            Thumbs = Sub(Config.Data.Thumbs),
            Logs = Sub(Config.Data.Logs),
            Backups = Sub(Config.Data.Backups),
            Trash = Sub(Config.Data.Trash)
        };
    }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            ModTypes = new List<ModTypeConfig>
            {
                new()
                {
                    Key = "bikes", Folder = "bikes", Title = "Bikes", Icon = "bike",
                    PairedFolder = true,
                    DefaultCategory = "Other",
                    CategoryRules =
                    {
                        new() { Name = "A-Kit", Pattern = "^AK[ _-]" },
                        new() { Name = "85cc", Pattern = "^85(cc)?[ _-]" },
                        new() { Name = "MX1 250/450", Pattern = "^MX1OEM" },
                        new() { Name = "MX2 125/250", Pattern = "^MX2OEM" },
                        new() { Name = "MX3 300/500", Pattern = "^MX3OEM" },
                        new() { Name = "Electric MX", Pattern = "^MXEOEM" },
                        new() { Name = "SM1 450", Pattern = "^SM1OEM" },
                        new() { Name = "SM2 250", Pattern = "^SM2OEM" },
                        new() { Name = "SM Classic", Pattern = "^SMCOEM" },
                        new() { Name = "Electric SM", Pattern = "^SMEOEM" },
                        new() { Name = "Adventure", Pattern = "^CRF1000" }
                    }
                },
                new()
                {
                    Key = "tracks", Folder = "tracks", Title = "Tracks", Icon = "track",
                    PairedFolder = true, InstallSubfolder = "Mods",
                    ScanSubfolders = { "Mods", "motocross", "supercross", "supermoto", "enduro" },
                    CategorizeBySubfolder = false
                },
                new()
                {
                    Key = "tyres", Folder = "tyres", Title = "Tyres", Icon = "tyre",
                    PairedFolder = true
                },
                new()
                {
                    Key = "rider", Folder = "rider", Title = "Rider", Icon = "rider",
                    ScanSubfolders = { "riders", "helmets", "boots", "protections", "animations", "helmetcams", "fonts" },
                    CategorizeBySubfolder = true
                },
                new()
                {
                    Key = "misc", Folder = "misc", Title = "Misc", Icon = "misc",
                    ScanSubfolders = { "fonts", "pitboard" },
                    CategorizeBySubfolder = true
                }
            }
        };
    }
}
