using MXBikesModManager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MXBikesModManager.Services;

public sealed class PlannedFile
{
    /// <summary>Entry key as stored in the archive (used to match while extracting).</summary>
    public required string EntryKey { get; init; }
    public required string DestinationPath { get; init; }
}

public sealed class ArchiveEntry
{
    public required string RawKey { get; init; }
    public required string RelativeKey { get; init; }
}

public sealed class InstallPlan
{
    public required string SourcePath { get; init; }
    public ModTypeConfig? Type { get; set; }
    public bool NeedsTypeChoice { get; set; }
    public bool IsDirectCopy { get; set; }
    public bool IsFolderCopy { get; set; }

    /// <summary>True when the dropped file looks like one or more bike paint jobs (.pnt).</summary>
    public bool IsBikePaint { get; set; }

    /// <summary>Paired bike folder (inside the bikes type folder) that receives the paints.</summary>
    public string? PaintBikeFolder { get; set; }

    /// <summary>Type guessed from the archive content, used to preselect the dialog.</summary>
    public string? DetectedTypeKey { get; set; }

    public string DetectedKind { get; set; } = string.Empty;
    public List<ArchiveEntry> Entries { get; } = new();
    public List<PlannedFile> Files { get; } = new();
    public List<string> Warnings { get; } = new();
    public int OverwriteCount { get; set; }

    public string SourceName => Path.GetFileName(SourcePath);
    public bool Ready => !NeedsTypeChoice && Type is not null && Files.Count > 0;

    /// <summary>Human-readable destination used in status messages.</summary>
    public string TargetLabel => IsBikePaint && !string.IsNullOrWhiteSpace(PaintBikeFolder)
        ? $"paints for {PaintBikeFolder}"
        : Type?.DisplayTitle ?? "mods";
}

public sealed record InstallProgress(int Done, int Total, string Current);

public sealed class InstallResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int InstalledFiles { get; init; }
    public int BackedUp { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Installs mods from .zip/.7z/.rar/.tar.gz archives, bare .pkz files or loose folders.
/// Existing files are never destroyed: they are moved to the repo data/backups folder first.
/// </summary>
public sealed class InstallService
{
    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".cab", ".lz"
    };

    private static readonly string[] WrapperNames = { "mods", "MX Bikes" };

    private readonly ConfigService _config;
    private readonly LogService _log;

    public InstallService(ConfigService config)
    {
        _config = config;
        _log = config.Log;
    }

    private string ModsRoot => _config.Config.Game.ModsRoot;

    public IReadOnlyList<ModTypeConfig> Types => _config.Config.ModTypes;

    public InstallPlan Analyze(string sourcePath, string? preferredTypeKey = null, string? preferredSubfolder = null)
    {
        var plan = new InstallPlan { SourcePath = sourcePath };

        if (Directory.Exists(sourcePath))
        {
            plan.IsFolderCopy = true;
            plan.IsDirectCopy = true;
            plan.DetectedKind = "folder";
            var folderType = ResolveType(preferredTypeKey);
            if (folderType is null) plan.NeedsTypeChoice = true;
            else Finalize(plan, folderType, preferredSubfolder);
            return plan;
        }

        if (!File.Exists(sourcePath))
        {
            plan.Warnings.Add("File not found.");
            return plan;
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (extension == ".pnt")
        {
            plan.IsDirectCopy = true;
            plan.IsBikePaint = true;
            plan.NeedsTypeChoice = true;
            plan.DetectedKind = "bike paint (.pnt)";
            return plan;
        }

        if (extension == ".pkz")
        {
            plan.IsDirectCopy = true;
            plan.DetectedKind = "pkz archive";
            var pkzType = ResolveType(preferredTypeKey);
            if (pkzType is null) plan.NeedsTypeChoice = true;
            else Finalize(plan, pkzType, preferredSubfolder);
            return plan;
        }

        if (!ArchiveExtensions.Contains(extension))
        {
            plan.Warnings.Add($"{extension} files are not supported. Use .zip, .7z, .rar, .pkz or a folder.");
            return plan;
        }

        List<string> rawKeys;
        try
        {
            using var archive = ArchiveFactory.OpenArchive(sourcePath, new ReaderOptions());
            rawKeys = archive.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => NormalizeKey(e.Key ?? string.Empty))
                .Where(k => k.Length > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error($"Cannot read archive {sourcePath}", ex);
            plan.Warnings.Add($"Could not open archive: {ex.Message}");
            return plan;
        }

        if (rawKeys.Count == 0)
        {
            plan.Warnings.Add("The archive is empty.");
            return plan;
        }

        plan.Entries.AddRange(MapEntries(rawKeys));
        var relativeKeys = plan.Entries.Select(e => e.RelativeKey).ToList();
        plan.DetectedKind = DescribeContent(relativeKeys);

        // Paint packs are .pnt files without a .pkz (often wrapped in a paints/ folder).
        // They need a bike choice rather than a type folder, so handle them before layout detection.
        if (LooksLikeBikePaint(relativeKeys))
        {
            plan.IsBikePaint = true;
            plan.NeedsTypeChoice = true;
            plan.DetectedKind = "bike paint (.pnt)";
            return plan;
        }

        // Case 1: the archive is already laid out like the game's mods folder (bikes/..., tracks/...),
        // optionally wrapped in a "mods" or "MX Bikes" folder.
        var firstSegments = relativeKeys
            .Select(k => k.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (firstSegments.Count == 1 && TryGetType(firstSegments[0], out var layoutType))
        {
            plan.Type = layoutType;
            plan.DetectedTypeKey = layoutType.Key;
            plan.NeedsTypeChoice = false;
            foreach (var entry in plan.Entries)
            {
                if (HasTraversal(entry.RelativeKey))
                {
                    plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                    continue;
                }
                var destination = Path.Combine(ModsRoot, entry.RelativeKey.Replace('/', Path.DirectorySeparatorChar));
                if (!IsInsideModsRoot(destination))
                {
                    plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                    continue;
                }
                plan.Files.Add(new PlannedFile { EntryKey = entry.RawKey, DestinationPath = destination });
            }
            CountOverwrites(plan);
            return plan;
        }

        var detected = DetectTypeKey(relativeKeys);
        plan.DetectedTypeKey = detected;
        var type = ResolveType(preferredTypeKey) ?? ResolveType(detected);
        if (type is null)
        {
            plan.NeedsTypeChoice = true;
            return plan;
        }

        Finalize(plan, type, preferredSubfolder);
        return plan;
    }

    /// <summary>Builds destination paths once the target type is known (also used after the user picks a type).</summary>
    public void Finalize(InstallPlan plan, ModTypeConfig type, string? preferredSubfolder = null)
    {
        plan.IsBikePaint = false;
        plan.PaintBikeFolder = null;
        plan.Type = type;
        plan.NeedsTypeChoice = false;
        plan.Files.Clear();

        var typeRoot = GetTypeRoot(type, preferredSubfolder);

        if (plan.IsFolderCopy)
        {
            var folderName = Path.GetFileName(plan.SourcePath.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Imported Mod";
            var destination = Path.Combine(typeRoot, folderName);
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add("Target folder escapes the mods directory; aborted.");
                return;
            }
            plan.Files.Add(new PlannedFile { EntryKey = "*", DestinationPath = destination });
            CountOverwrites(plan);
            return;
        }

        if (plan.IsDirectCopy)
        {
            var destination = Path.Combine(typeRoot, Path.GetFileName(plan.SourcePath));
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add("Target path escapes the mods directory; aborted.");
                return;
            }
            plan.Files.Add(new PlannedFile { EntryKey = Path.GetFileName(plan.SourcePath), DestinationPath = destination });
            CountOverwrites(plan);
            return;
        }

        if (plan.Entries.Count == 0) return;

        var keys = plan.Entries.Select(e => e.RelativeKey).ToList();
        var onlyPkz = keys.All(k => k.EndsWith(".pkz", StringComparison.OrdinalIgnoreCase));
        var commonRoot = GetCommonRoot(keys);
        var wrap = !onlyPkz && commonRoot is null;
        var wrapName = Path.GetFileNameWithoutExtension(plan.SourcePath);
        if (string.IsNullOrWhiteSpace(wrapName)) wrapName = "Imported Mod";

        foreach (var entry in plan.Entries)
        {
            if (HasTraversal(entry.RelativeKey))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }
            var relative = wrap ? Path.Combine(wrapName, entry.RelativeKey) : entry.RelativeKey;
            var destination = Path.Combine(typeRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }
            plan.Files.Add(new PlannedFile { EntryKey = entry.RawKey, DestinationPath = destination });
        }

        CountOverwrites(plan);
    }

    /// <summary>Bike folders (paired with a .pkz) that can receive paint jobs.</summary>
    public IReadOnlyList<string> GetPaintBikes()
    {
        var bikes = ResolveType("bikes");
        if (bikes is null) return Array.Empty<string>();

        var bikesRoot = Path.Combine(ModsRoot, bikes.Folder);
        if (!Directory.Exists(bikesRoot)) return Array.Empty<string>();

        var suffix = _config.Config.Mods.DisabledSuffix;
        return PathUtil.EnumerateDirectoriesSafe(bikesRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('.'))
            .Select(name => name!)
            .Select(name => StripDisabledSuffix(name, suffix))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Picks the bike whose name appears in the dropped file name, if there is an obvious match.</summary>
    public static string? SuggestPaintBike(string sourcePath, IEnumerable<string> bikes)
    {
        var haystack = NormalizeForMatch(Path.GetFileNameWithoutExtension(sourcePath));
        if (haystack.Length == 0) return null;

        return bikes
            .Select(name => (Name: name, Needle: NormalizeForMatch(name)))
            .Where(candidate => candidate.Needle.Length >= 3 && haystack.Contains(candidate.Needle, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Needle.Length)
            .Select(candidate => candidate.Name)
            .FirstOrDefault();
    }

    /// <summary>Targets a paint archive/file at bikes/&lt;bike&gt;/paints/.</summary>
    public bool FinalizePaint(InstallPlan plan, string bikeFolder)
    {
        plan.Type = ResolveType("bikes");
        plan.NeedsTypeChoice = false;
        plan.IsBikePaint = true;
        plan.PaintBikeFolder = bikeFolder;
        plan.Files.Clear();

        if (string.IsNullOrWhiteSpace(bikeFolder) ||
            bikeFolder.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
            bikeFolder is "." or "..")
        {
            plan.Warnings.Add("Invalid bike folder name.");
            return false;
        }

        if (plan.Type is null)
        {
            plan.Warnings.Add("The bikes mod type is not configured.");
            return false;
        }

        var bikeRoot = Path.Combine(ModsRoot, plan.Type.Folder, bikeFolder);

        // If the bike is currently disabled its asset folder carries the disabled suffix;
        // keep the paint there so it travels with the folder when it is re-enabled.
        var suffix = _config.Config.Mods.DisabledSuffix;
        if (!Directory.Exists(bikeRoot) && !string.IsNullOrEmpty(suffix) &&
            Directory.Exists(bikeRoot + suffix))
        {
            bikeRoot += suffix;
        }

        var paintsRoot = Path.Combine(bikeRoot, "paints");
        if (!IsInsideModsRoot(paintsRoot))
        {
            plan.Warnings.Add("Target path escapes the mods directory; aborted.");
            return false;
        }

        if (!Directory.Exists(bikeRoot))
        {
            plan.Warnings.Add($"Bike folder “{bikeFolder}” does not exist yet; it will be created.");
        }

        if (plan.IsDirectCopy)
        {
            var destination = Path.Combine(paintsRoot, Path.GetFileName(plan.SourcePath));
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add("Target path escapes the mods directory; aborted.");
                return false;
            }
            plan.Files.Add(new PlannedFile
            {
                EntryKey = Path.GetFileName(plan.SourcePath),
                DestinationPath = destination
            });
            CountOverwrites(plan);
            return true;
        }

        foreach (var entry in plan.Entries)
        {
            if (HasTraversal(entry.RelativeKey))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }

            var relative = PaintRelativePath(entry.RelativeKey);
            if (relative.Length == 0) continue;

            var destination = Path.Combine(paintsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }
            plan.Files.Add(new PlannedFile { EntryKey = entry.RawKey, DestinationPath = destination });
        }

        CountOverwrites(plan);
        return plan.Files.Count > 0;
    }

    /// <summary>Drops everything up to a paints/ folder so paint packs land flat in the bike's paints dir.</summary>
    private static string PaintRelativePath(string key)
    {
        var segments = key.Split('/');
        var paintsIndex = Array.FindIndex(
            segments,
            segment => string.Equals(segment, "paints", StringComparison.OrdinalIgnoreCase));

        if (paintsIndex >= 0 && paintsIndex + 1 < segments.Length)
        {
            return string.Join('/', segments.Skip(paintsIndex + 1));
        }

        return Path.GetFileName(key);
    }

    private static bool LooksLikeBikePaint(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0) return false;

        var hasPaint = false;
        foreach (var key in keys)
        {
            var extension = Path.GetExtension(key).ToLowerInvariant();
            if (extension is ".pkz" or ".map" or ".trh" or ".amb" or ".tyre") return false;
            if (extension == ".pnt") hasPaint = true;
        }

        return hasPaint;
    }

    private static string StripDisabledSuffix(string name, string suffix)
    {
        if (string.IsNullOrEmpty(suffix) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name;
        return name[..^suffix.Length];
    }

    private static string NormalizeForMatch(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        }
        return builder.ToString();
    }

    public async Task<InstallResult> InstallAsync(
        InstallPlan plan,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!plan.Ready)
        {
            return new InstallResult { Success = false, Message = "The install plan is incomplete." };
        }

        try
        {
            return plan.IsDirectCopy
                ? await Task.Run(() => InstallSingle(plan, progress, ct), ct)
                : await Task.Run(() => InstallArchive(plan, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult { Success = false, Message = "Install cancelled." };
        }
        catch (Exception ex)
        {
            _log.Error("Install failed", ex);
            return new InstallResult { Success = false, Message = $"Install failed: {ex.Message}" };
        }
    }

    private InstallResult InstallSingle(InstallPlan plan, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var planned = plan.Files[0];
        var backups = 0;

        ct.ThrowIfCancellationRequested();
        progress?.Report(new InstallProgress(0, 1, planned.DestinationPath));

        if (plan.IsFolderCopy)
        {
            if (Directory.Exists(planned.DestinationPath)) backups += Backup(planned.DestinationPath);
            else if (File.Exists(planned.DestinationPath)) backups += Backup(planned.DestinationPath);
            CopyDirectory(plan.SourcePath, planned.DestinationPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(planned.DestinationPath)!);
            if (File.Exists(planned.DestinationPath)) backups += Backup(planned.DestinationPath);
            File.Copy(plan.SourcePath, planned.DestinationPath, overwrite: true);
        }

        progress?.Report(new InstallProgress(1, 1, planned.DestinationPath));
        _log.Info($"Installed {plan.SourceName} → {planned.DestinationPath}");
        return new InstallResult
        {
            Success = true,
            Message = $"Installed “{plan.SourceName}” into {plan.TargetLabel}.",
            InstalledFiles = 1,
            BackedUp = backups
        };
    }

    private InstallResult InstallArchive(InstallPlan plan, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var planned = plan.Files.ToDictionary(f => f.EntryKey, f => f.DestinationPath, StringComparer.OrdinalIgnoreCase);
        var total = plan.Files.Count;
        var done = 0;
        var backups = 0;
        var errors = new List<string>();

        void ExtractTo(string key, Stream input)
        {
            if (!planned.TryGetValue(key, out var destination)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination)) backups += Backup(destination);
                else if (Directory.Exists(destination)) backups += Backup(destination);

                using var output = File.Create(destination);
                input.CopyTo(output);

                done++;
                progress?.Report(new InstallProgress(done, total, key));
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to extract {key}", ex);
                errors.Add($"{key}: {ex.Message}");
            }
        }

        using var archive = ArchiveFactory.OpenArchive(plan.SourcePath, new ReaderOptions());

        if (archive.IsSolid)
        {
            // Solid archives (e.g. some 7z/rar files) must be streamed front-to-back.
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;
                var key = NormalizeKey(reader.Entry.Key ?? string.Empty);
                if (!planned.ContainsKey(key)) continue;
                using var input = reader.OpenEntryStream();
                ExtractTo(key, input);
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;

                var key = NormalizeKey(entry.Key ?? string.Empty);
                if (!planned.ContainsKey(key)) continue;

                using var input = entry.OpenEntryStream();
                ExtractTo(key, input);
            }
        }

        var success = errors.Count == 0 && done > 0;
        _log.Info($"Installed {plan.SourceName}: {done}/{total} files, {backups} backed up");
        return new InstallResult
        {
            Success = success,
            Message = success
                ? $"Installed “{plan.SourceName}” into {plan.TargetLabel} ({done} files)."
                : $"Install finished with problems ({done}/{total} files).",
            InstalledFiles = done,
            BackedUp = backups,
            Errors = errors
        };
    }

    private string GetTypeRoot(ModTypeConfig type, string? preferredSubfolder)
    {
        var root = Path.Combine(ModsRoot, type.Folder);
        var subfolder = !string.IsNullOrWhiteSpace(preferredSubfolder)
            ? preferredSubfolder
            : type.InstallSubfolder;

        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            root = Path.Combine(root, subfolder);
        }
        return root;
    }

    private ModTypeConfig? ResolveType(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return _config.Config.ModTypes.FirstOrDefault(t =>
            string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Folder, key, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetType(string folderOrKey, out ModTypeConfig type)
    {
        var found = ResolveType(folderOrKey);
        type = found!;
        return found is not null;
    }

    private static string? DetectTypeKey(IEnumerable<string> keys)
    {
        var extensions = keys
            .Select(k => Path.GetExtension(k).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (extensions.Contains(".map") || extensions.Contains(".trh") || extensions.Contains(".amb")) return "tracks";
        if (extensions.Contains(".tyre")) return "tyres";
        if (extensions.Contains(".pnt") || extensions.Contains(".scl") || extensions.Contains(".skn")) return "bikes";
        return null;
    }

    private static string DescribeContent(IEnumerable<string> keys)
    {
        var extensions = keys
            .Select(k => Path.GetExtension(k).ToLowerInvariant())
            .Where(e => e.Length > 1)
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .Take(4)
            .Select(g => g.Key)
            .ToList();
        return extensions.Count == 0 ? "unknown archive" : "contains " + string.Join(", ", extensions);
    }

    private List<ArchiveEntry> MapEntries(List<string> rawKeys)
    {
        var wrapper = rawKeys[0].Split('/')[0];
        var hasWrapper = WrapperNames.Any(w => string.Equals(w, wrapper, StringComparison.OrdinalIgnoreCase))
                         && rawKeys.All(k => k.Contains('/'));

        return rawKeys.Select(raw =>
        {
            var relative = hasWrapper ? raw[(wrapper.Length + 1)..] : raw;
            return new ArchiveEntry { RawKey = raw, RelativeKey = relative };
        }).ToList();
    }

    private static bool HasTraversal(string key)
        => key.Split('/').Any(segment => segment is ".." or "." or "");

    private static string? GetCommonRoot(List<string> keys)
    {
        var first = keys[0].Split('/')[0];
        if (keys[0].Contains('/') && keys.All(k => k.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return first;
        }
        return null;
    }

    public static string NormalizeKey(string key)
    {
        var normalized = key.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private bool IsInsideModsRoot(string path)
    {
        var root = Path.GetFullPath(ModsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.Ordinal);
    }

    private void CountOverwrites(InstallPlan plan)
    {
        plan.OverwriteCount = plan.Files.Count(f => File.Exists(f.DestinationPath) || Directory.Exists(f.DestinationPath));
    }

    private int Backup(string path)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var relative = Path.GetRelativePath(ModsRoot, path);
            var target = Path.Combine(_config.Data.Backups, stamp, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (Directory.Exists(path)) Directory.Move(path, target);
            else File.Move(path, target);
            return 1;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not back up {path}: {ex.Message}");
            return 0;
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
}
