using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

public sealed record OperationResult(bool Success, string Message);

/// <summary>
/// Enables/disables mods by renaming in place (instant, no copying of multi-GB files)
/// and moves deleted mods to the repo data folder instead of destroying them.
/// </summary>
public sealed class ToggleService
{
    private readonly ConfigService _config;

    public ToggleService(ConfigService config) => _config = config;

    public string DisabledSuffix => _config.Config.Mods.DisabledSuffix;

    public OperationResult SetEnabled(ModEntry entry, bool enabled)
    {
        var primaryTarget = Adjust(entry.Path, enabled);
        var pairedTarget = entry.PairedFolderPath is null ? null : Adjust(entry.PairedFolderPath, enabled);

        if (PathsEqual(entry.Path, primaryTarget) &&
            (pairedTarget is null || PathsEqual(entry.PairedFolderPath!, pairedTarget)))
        {
            return new OperationResult(true, "Already in the requested state.");
        }

        if (EntryExists(primaryTarget))
        {
            return new OperationResult(false, $"{Path.GetFileName(primaryTarget)} already exists — nothing was changed.");
        }

        if (pairedTarget is not null && EntryExists(pairedTarget))
        {
            return new OperationResult(false, $"{Path.GetFileName(pairedTarget)} already exists — nothing was changed.");
        }

        try
        {
            // Rename the asset folder first; restore it if the primary rename fails.
            if (pairedTarget is not null)
            {
                MoveEntry(entry.PairedFolderPath!, pairedTarget);
            }

            try
            {
                MoveEntry(entry.Path, primaryTarget);
            }
            catch
            {
                if (pairedTarget is not null && EntryExists(pairedTarget))
                {
                    TryMoveBack(pairedTarget, entry.PairedFolderPath!);
                }
                throw;
            }

            entry.Path = primaryTarget;
            if (pairedTarget is not null) entry.PairedFolderPath = pairedTarget;
            entry.IsEnabled = enabled;

            var action = enabled ? "Enabled" : "Disabled";
            _config.Log.Info($"{action} {entry.Type.Folder}/{Path.GetFileName(primaryTarget)}");
            return new OperationResult(true, $"{action} {entry.DisplayName}");
        }
        catch (Exception ex)
        {
            _config.Log.Error("Toggle failed", ex);
            return new OperationResult(false, $"Could not change state: {ex.Message}");
        }
    }

    public OperationResult MoveToTrash(ModEntry entry)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var targetDir = Path.Combine(_config.Data.Trash, stamp, entry.Type.Folder);
            if (!string.IsNullOrEmpty(entry.Category)) targetDir = Path.Combine(targetDir, entry.Category);
            Directory.CreateDirectory(targetDir);

            var paths = new List<string> { entry.Path };
            if (entry.PairedFolderPath is not null) paths.Add(entry.PairedFolderPath);

            foreach (var path in paths)
            {
                var target = Path.Combine(targetDir, Path.GetFileName(path));
                MoveOrCopy(path, target);
            }

            _config.Log.Info($"Moved to trash: {entry.Path}");
            return new OperationResult(true, $"Moved “{entry.DisplayName}” to data/trash.");
        }
        catch (Exception ex)
        {
            _config.Log.Error("Move to trash failed", ex);
            return new OperationResult(false, $"Could not move to trash: {ex.Message}");
        }
    }

    private string Adjust(string path, bool enabled)
    {
        var suffix = DisabledSuffix;
        if (suffix.Length == 0) return path;
        var isDisabled = path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        if (enabled && isDisabled) return path[..^suffix.Length];
        if (!enabled && !isDisabled) return path + suffix;
        return path;
    }

    private static void MoveEntry(string source, string target)
    {
        if (File.Exists(source))
        {
            File.Move(source, target);
        }
        else if (Directory.Exists(source))
        {
            Directory.Move(source, target);
        }
        else
        {
            throw new FileNotFoundException("Mod path not found", source);
        }
    }

    private static void TryMoveBack(string from, string to)
    {
        try
        {
            MoveEntry(from, to);
        }
        catch (Exception)
        {
            // Best effort rollback.
        }
    }

    private static void MoveOrCopy(string source, string target)
    {
        if (File.Exists(source))
        {
            try
            {
                File.Move(source, target);
            }
            catch (IOException)
            {
                // Cross-device move: copy then delete.
                File.Copy(source, target, overwrite: true);
                File.Delete(source);
            }
        }
        else if (Directory.Exists(source))
        {
            try
            {
                Directory.Move(source, target);
            }
            catch (IOException)
            {
                CopyDirectory(source, target);
                Directory.Delete(source, recursive: true);
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static bool EntryExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
