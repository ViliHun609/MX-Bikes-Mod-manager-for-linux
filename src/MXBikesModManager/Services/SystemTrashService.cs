using System.Diagnostics;

namespace MXBikesModManager.Services;

public sealed record TrashResult(bool Success, string Message);

/// <summary>Moves an installed source file or folder to the desktop's system trash.</summary>
public interface ITrashService
{
    Task<TrashResult> TrashAsync(string path);
}

/// <summary>
/// Uses <c>gio trash</c> (the same XDG trash Nautilus shows), with <c>trash-put</c> as a
/// fallback. Never permanently deletes anything, and refuses to touch the app's own files.
/// </summary>
public sealed class SystemTrashService : ITrashService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ConfigService _config;
    private readonly LogService _log;

    public SystemTrashService(ConfigService config)
    {
        _config = config;
        _log = config.Log;
    }

    public async Task<TrashResult> TrashAsync(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return Fail($"Invalid source path: {ex.Message}");
        }

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            return Fail("The source no longer exists.");
        }

        if (!CanTrash(full, _config.Config.Game.ModsRoot, _config.Data.Root, _config.ConfigPath, out var reason))
        {
            _log.Warn($"Refusing to trash {full}: {reason}");
            return Fail(reason);
        }

        var hasGio = CommandExists("gio");
        var hasTrashPut = CommandExists("trash-put");

        if (!hasGio && !hasTrashPut)
        {
            _log.Warn("Cannot move installed source to Trash: neither gio nor trash-put is available.");
            return Fail("The system trash tool (gio or trash-put) was not found; the source was kept.");
        }

        TrashResult? result = null;

        if (hasGio)
        {
            result = await RunAsync("gio", new[] { "trash", "--", full }, full);
            if (result.Success)
            {
                _log.Info($"Moved source to system Trash: {full}");
                return new TrashResult(true, $"Moved “{Path.GetFileName(full)}” to the system Trash.");
            }
        }

        if (hasTrashPut)
        {
            var fallback = await RunAsync("trash-put", new[] { full }, full);
            if (fallback.Success)
            {
                _log.Info($"Moved source to system Trash: {full}");
                return new TrashResult(true, $"Moved “{Path.GetFileName(full)}” to the system Trash.");
            }

            result = fallback;
        }

        _log.Warn($"Could not move {full} to the system Trash: {result?.Message}");
        return Fail($"Could not move the source to Trash: {result?.Message ?? "unknown error"}");
    }

    /// <summary>True when the path is safe to move to the system trash.</summary>
    public static bool CanTrash(
        string path,
        string modsRoot,
        string dataRoot,
        string configPath,
        out string reason)
    {
        reason = string.Empty;

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception)
        {
            reason = "Invalid source path.";
            return false;
        }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            reason = "Refusing to trash a filesystem root.";
            return false;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home) && IsSameOrInside(home, full))
        {
            reason = "Refusing to trash your home directory.";
            return false;
        }

        if (IsSameOrInside(full, modsRoot))
        {
            reason = "That path is inside the mods folder; the source was kept.";
            return false;
        }

        if (IsSameOrInside(full, dataRoot))
        {
            reason = "That path is inside the app data folder; the source was kept.";
            return false;
        }

        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath));
        if (!string.IsNullOrWhiteSpace(configDir) && IsSameOrInside(full, configDir))
        {
            reason = "That path is inside the app config folder; the source was kept.";
            return false;
        }

        // Also refuse a folder that contains the mods/data/config trees (e.g. dragging "Documents").
        if (Directory.Exists(full))
        {
            foreach (var protectedPath in new[] { modsRoot, dataRoot, configDir })
            {
                if (!string.IsNullOrWhiteSpace(protectedPath) && IsSameOrInside(protectedPath, full))
                {
                    reason = "That folder contains the manager's data; the source was kept.";
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsSameOrInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        return string.Equals(full, basePath, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CommandExists(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (!File.Exists(candidate)) continue;

            if (OperatingSystem.IsWindows()) return true;

            try
            {
                var mode = File.GetUnixFileMode(candidate);
                if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Not a Unix filesystem; fall back to the existence check.
                return true;
            }
        }

        return false;
    }

    private async Task<TrashResult> RunAsync(string command, IReadOnlyList<string> arguments, string source)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null) return new TrashResult(false, "could not start the trash command");

            var stderrTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(Timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new TrashResult(false, "the trash command timed out");
            }

            var stderr = (await stderrTask).Trim();
            if (process.ExitCode == 0)
            {
                return new TrashResult(true, string.Empty);
            }

            return new TrashResult(false,
                stderr.Length > 0 ? stderr : $"{command} exited with code {process.ExitCode}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Trash command failed for {source}: {ex.Message}");
            return new TrashResult(false, ex.Message);
        }
    }

    private TrashResult Fail(string message) => new(false, message);
}
