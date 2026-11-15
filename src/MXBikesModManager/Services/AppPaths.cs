namespace MXBikesModManager.Services;

public static class AppPaths
{
    /// <summary>Set by the AppImage runtime to the path of the .AppImage file.</summary>
    public static string? AppImagePath
    {
        get
        {
            var p = Environment.GetEnvironmentVariable("APPIMAGE");
            return string.IsNullOrWhiteSpace(p) ? null : p;
        }
    }

    public static string ExecutableDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>Directory the app was launched from: next to the AppImage, or the binary folder.</summary>
    public static string LaunchDir
    {
        get
        {
            var appImage = AppImagePath;
            if (appImage is not null)
            {
                var dir = Path.GetDirectoryName(appImage);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            return ExecutableDir;
        }
    }

    /// <summary>
    /// Config lookup order:
    /// 1. explicit --config path
    /// 2. appsettings.json next to the AppImage / executable
    /// 3. appsettings.json in the current directory
    /// 4. appsettings.json in any parent folder (covers running from bin/Debug/net10.0)
    /// </summary>
    public static string? FindConfigPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var candidates = new List<string>
        {
            Path.Combine(LaunchDir, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        };

        DirectoryInfo? dir = new(LaunchDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "appsettings.json"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }
}
