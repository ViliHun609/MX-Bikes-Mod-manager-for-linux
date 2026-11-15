using System.Text.RegularExpressions;

namespace MXBikesModManager.Services;

public sealed record SteamDetectionResult(string? ModsRoot, IReadOnlyList<string> Searched, IReadOnlyList<string> Problems);

/// <summary>
/// Finds the MX Bikes mods folder inside a Proton prefix, across all Steam libraries
/// (native, Flatpak and manually added library folders).
/// </summary>
public static partial class SteamLocator
{
    public static SteamDetectionResult FindModsRoot(string appId)
        => FindModsRoot(appId, EnumerateLibraries());

    /// <summary>Testable core: searches the given steamapps folders for the app's Proton prefix.</summary>
    public static SteamDetectionResult FindModsRoot(string appId, IEnumerable<string> steamAppsDirectories)
    {
        var searched = new List<string>();
        var problems = new List<string>();

        foreach (var steamApps in steamAppsDirectories)
        {
            var pfx = Path.Combine(steamApps, "compatdata", appId, "pfx", "drive_c", "users");
            searched.Add(pfx);

            var mods = FindModsFolderInPrefix(pfx);
            if (mods is not null)
            {
                return new SteamDetectionResult(mods, searched, problems);
            }
        }

        if (searched.Count == 0)
        {
            problems.Add("No Steam library was found.");
        }

        return new SteamDetectionResult(null, searched, problems);
    }

    /// <summary>Looks for the mods folder in one Steam library's Proton prefix for the given app id.</summary>
    public static string? FindModsRootInLibrary(string steamAppsDirectory, string appId)
    {
        var pfx = Path.Combine(steamAppsDirectory, "compatdata", appId, "pfx", "drive_c", "users");
        return FindModsFolderInPrefix(pfx);
    }

    /// <summary>Checks each Proton user folder for My Documents/PiBoSo/MX Bikes/mods.</summary>
    public static string? FindModsFolderInPrefix(string pfxUsersDirectory)
    {
        if (!Directory.Exists(pfxUsersDirectory)) return null;

        foreach (var userDir in PathUtil.EnumerateDirectoriesSafe(pfxUsersDirectory))
        {
            foreach (var documentsName in new[] { "My Documents", "Documents", "Documents and Settings" })
            {
                var mods = PathUtil.ResolveIgnoreCase(userDir, documentsName, "PiBoSo", "MX Bikes", "mods");
                if (mods is not null && Directory.Exists(mods))
                {
                    return mods;
                }
            }
        }

        return null;
    }

    public static IEnumerable<string> EnumerateLibraries()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var steamRoot in EnumerateSteamRoots())
        {
            var steamapps = Path.Combine(steamRoot, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            if (seen.Add(steamapps)) yield return steamapps;

            var vdf = Path.Combine(steamapps, "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (Match match in PathEntryRegex().Matches(text))
            {
                var path = match.Groups["path"].Value.Replace("\\\\", "\\");
                var librarySteamApps = Path.Combine(path, "steamapps");
                if (Directory.Exists(librarySteamApps) && seen.Add(librarySteamApps))
                {
                    yield return librarySteamApps;
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateSteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".steam", "root"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
            "/usr/share/steam",
            "/usr/local/share/steam"
        };

        var snap = Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");
        candidates.Add(snap);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full) && seen.Add(full))
            {
                yield return full;
            }
        }
    }

    [GeneratedRegex("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntryRegex();
}
