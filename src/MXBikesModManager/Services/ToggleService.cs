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
        var primaryNeedsMove = !PathsEqual(entry.Path, primaryTarget);

        var pairedSource = entry.PairedFolderPath;
        var pairedTarget = pairedSource is null ? null : Adjust(pairedSource, enabled);
        var pairedNeedsMove = pairedSource is not null &&
                              pairedTarget is not null &&
                              !PathsEqual(pairedSource, pairedTarget);

        if (!primaryNeedsMove && !pairedNeedsMove)
        {
            // The files may already be in the requested state even if the caller thought
            // otherwise (e.g. the folder was toggled outside the app); keep both in sync.
            entry.IsEnabled = enabled;
            return new OperationResult(true, "Already in the requested state.");
        }

        if (primaryNeedsMove && EntryExists(primaryTarget))
        {
            return new OperationResult(false, $"{Path.GetFileName(primaryTarget)} already exists — nothing was changed.");
        }

        if (pairedNeedsMove && EntryExists(pairedTarget!))
        {
            return new OperationResult(false, $"{Path.GetFileName(pairedTarget!)} already exists — nothing was changed.");
        }

        try
        {
            // Rename the asset folder first; restore it if the primary rename fails.
            if (pairedNeedsMove)
            {
                MoveEntry(pairedSource!, pairedTarget!);
            }

            try
            {
                if (primaryNeedsMove)
                {
                    MoveEntry(entry.Path, primaryTarget);
                }
            }
            catch
            {
                if (pairedNeedsMove && EntryExists(pairedTarget!))
                {
                    TryMoveBack(pairedTarget!, pairedSource!);
                }
                throw;
            }

            if (primaryNeedsMove) entry.Path = primaryTarget;
            if (pairedNeedsMove) entry.PairedFolderPath = pairedTarget;
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
            var targetDir = Path.Combine(_config.Data.Trash, stamp, SafeFolderName(entry.Type.Folder));
            var category = SafeFolderName(entry.Category);
            if (category.Length > 0) targetDir = Path.Combine(targetDir, category);
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
        else
        {
            throw new FileNotFoundException("Mod path not found", source);
        }
    }

    /// <summary>Turns a category/type name into a single safe folder name ("MX1 250/450" → "MX1 250-450").</summary>
    private static string SafeFolderName(string value)
    {
        var name = value.Replace('/', '-').Replace('\\', '-').Trim();
        return name is "." or ".." ? string.Empty : name;
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
