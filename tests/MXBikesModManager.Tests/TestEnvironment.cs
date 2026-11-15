using System.Text.Json;
using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.Tests;

/// <summary>Creates a throwaway mods tree + appsettings.json for a test.</summary>
public sealed class TestEnvironment : IDisposable
{
    public string Root { get; }
    public string ModsRoot { get; }
    public ConfigService Config { get; }

    public TestEnvironment(Action<AppConfig>? customize = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "mxbikes-tests", Guid.NewGuid().ToString("N"));
        ModsRoot = Path.Combine(Root, "mods");
        Directory.CreateDirectory(ModsRoot);

        foreach (var type in new[] { "bikes", "tracks", "tyres", "rider", "misc" })
        {
            Directory.CreateDirectory(Path.Combine(ModsRoot, type));
        }
        Directory.CreateDirectory(Path.Combine(ModsRoot, "tracks", "Mods"));
        Directory.CreateDirectory(Path.Combine(ModsRoot, "rider", "riders"));
        Directory.CreateDirectory(Path.Combine(ModsRoot, "rider", "helmets"));

        var config = ConfigService.CreateDefault();
        config.Game.ModsRoot = ModsRoot;
        config.Game.AutoDetect = false;
        customize?.Invoke(config);

        var configPath = Path.Combine(Root, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));

        Config = ConfigService.Load(configPath);
    }

    public string Touch(string relativePath, string content = "x")
    {
        var path = Path.Combine(ModsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string MakeDirectory(string relativePath)
    {
        var path = Path.Combine(ModsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public ModTypeConfig Type(string key) => Config.Config.ModTypes.First(t => t.Key == key);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}

/// <summary>A clean temporary directory that deletes itself on Dispose.</summary>
public sealed class TestDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mxbikes-tests", Guid.NewGuid().ToString("N"));

    public TestDir() => Directory.CreateDirectory(Path);

    public string Dir(params string[] segments)
    {
        var path = segments.Aggregate(Path, System.IO.Path.Combine);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
