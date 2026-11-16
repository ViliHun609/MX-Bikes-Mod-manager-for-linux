using MXBikesModManager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MXBikesModManager.Services;

public sealed class PlannedFile
{
    /// <summary>Entry key as stored in the archive (used to match while extracting).</summary>
    public required string EntryKey { get; init; }

    /// <summary>Absolute source path for folder-layout installs; null for archive entries.</summary>
    public string? SourcePath { get; init; }

    public required string DestinationPath { get; init; }
}

public sealed class ArchiveEntry
{
    public required string RawKey { get; init; }
    public required string RelativeKey { get; init; }

    /// <summary>Absolute source path for folder-layout plans; null for archive entries.</summary>
    public string? SourcePath { get; init; }
}

public sealed class InstallPlan
{
    public required string SourcePath { get; init; }
    public ModTypeConfig? Type { get; set; }
    public bool NeedsTypeChoice { get; set; }
    public bool IsDirectCopy { get; set; }
    public bool IsFolderCopy { get; set; }

    /// <summary>True when a dropped folder already contains a game mods/&lt;type&gt;/... tree.</summary>
    public bool IsLayoutFolder { get; set; }

    /// <summary>True when the dropped file looks like one or more bike paint jobs (.pnt).</summary>
    public bool IsBikePaint { get; set; }

    /// <summary>Paired bike folder (inside the bikes type folder) that receives the paints.</summary>
    public string? PaintBikeFolder { get; set; }

    /// <summary>Type guessed from the archive content, used to preselect the dialog.</summary>
    public string? DetectedTypeKey { get; set; }

    /// <summary>True when the archive already uses the game's mods/&lt;type&gt;/... layout.</summary>
    public bool HasGameLayout { get; set; }
    public string? LayoutTypeKey { get; set; }

    /// <summary>Known subfolder found after the type, e.g. "riders" or "Mods".</summary>
    public string? LayoutSubfolder { get; set; }

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
            // Sample first so a huge normal folder does not get fully walked just to detect
            // packaging. If a layout is found, re-enumerate everything before planning.
            var folderSample = EnumerateFolderKeys(sourcePath, limit: 500);
            var folderLayout = DetectLayout(folderSample);
            if (folderLayout is not null)
            {
                var folderKeys = EnumerateFolderKeys(sourcePath);
                if (IsLayoutDominant(folderKeys, folderLayout.Prefix))
                {
                    ApplyLayout(plan, folderLayout, sourcePath, folderKeys, isFolder: true);
                    return plan;
                }
            }

            plan.IsFolderCopy = true;
            plan.IsDirectCopy = true;
            plan.DetectedKind = "folder";
            plan.DetectedTypeKey = SuggestTypeKey(sourcePath, preferredTypeKey);
            plan.NeedsTypeChoice = true;
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
            plan.DetectedKind = "PiBoSo .pkz archive (type cannot be read)";
            plan.DetectedTypeKey = SuggestTypeKey(sourcePath, preferredTypeKey);
            plan.NeedsTypeChoice = true;
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

        // Loose-file detection needs the wrapper-stripped names, but the layout detector
        // deliberately looks at the original keys so it can see "mods/rider/riders/...".
        var mapped = MapEntries(rawKeys);
        var relativeKeys = mapped.Select(e => e.RelativeKey).ToList();
        plan.DetectedKind = DescribeContent(relativeKeys);

        // Case 1: the archive already contains a game layout, possibly wrapped several levels
        // deep (mods/rider/riders/..., MX Bikes/mods/rider/..., DownloadPack/mods/rider/...).
        var layout = DetectLayout(rawKeys);

        // Paint packs are .pnt files without a .pkz (often wrapped in a paints/ folder).
        // They use the bike picker, but only when this is not a rider/track/etc. layout.
        // Rider gear paints (rider/riders/.../paints) are normal layout files.
        if ((layout is null || string.Equals(layout.Type.Key, "bikes", StringComparison.OrdinalIgnoreCase)) &&
            LooksLikeBikePaint(relativeKeys))
        {
            plan.Entries.AddRange(mapped);
            plan.IsBikePaint = true;
            plan.NeedsTypeChoice = true;
            plan.DetectedKind = "bike paint (.pnt)";
            return plan;
        }

        if (layout is not null)
        {
            ApplyLayout(plan, layout, sourcePath, rawKeys, isFolder: false);
            return plan;
        }

        plan.Entries.AddRange(mapped);

        var detected = DetectTypeKey(relativeKeys);
        var hint = SuggestTypeKey(sourcePath, preferredTypeKey);
        plan.DetectedTypeKey = detected ?? hint;
        var type = ResolveType(detected) ?? ResolveType(hint);
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
        plan.Warnings.Clear();

        var typeRoot = GetTypeRoot(type, preferredSubfolder);

        if (plan.HasGameLayout)
        {
            BuildLayoutFiles(plan, type, preferredSubfolder);
            return;
        }

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

        // Avoid rider/riders/riders/... when the pack already starts with the chosen subfolder.
        var subfolderToStrip = preferredSubfolder?.Trim();
        var stripLeadingSubfolder = !string.IsNullOrWhiteSpace(subfolderToStrip) &&
            plan.Entries.Count > 0 &&
            plan.Entries.All(e => e.RelativeKey.StartsWith(
                subfolderToStrip + "/", StringComparison.OrdinalIgnoreCase));
        var stripLength = stripLeadingSubfolder ? subfolderToStrip!.Length + 1 : 0;

        foreach (var entry in plan.Entries)
        {
            if (HasTraversal(entry.RelativeKey))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }

            var entryRelative = stripLength > 0 ? entry.RelativeKey[stripLength..] : entry.RelativeKey;
            if (entryRelative.Length == 0) continue;

            var relative = wrap ? Path.Combine(wrapName, entryRelative) : entryRelative;
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

    private static readonly (string Folder, string[] Hints)[] SubfolderHints =
    {
        ("boots", new[] { "boot", "instinct", "crossfire", "tech10", "tech 10", "gaerne", "sg12", "sg-12" }),
        ("helmetcams", new[] { "helmetcam", "helmet cam", "camera", "gopro" }),
        ("helmets", new[] { "helmet", "goggle", "mx9", "v3 helmet" }),
        ("protections", new[] { "protection", "protector", "brace", "chest", "knee", "elbow", "roost" }),
        ("riders", new[] { "rider", "jersey", "pants", "gear", "body", "skin", "model" }),
        ("animations", new[] { "animation", "anim" }),
        ("fonts", new[] { "font" }),
        ("pitboard", new[] { "pitboard", "pit board" }),
        ("motocross", new[] { "motocross", "mx track" }),
        ("supercross", new[] { "supercross", "sx track" }),
        ("supermoto", new[] { "supermoto", "sm track" }),
        ("enduro", new[] { "enduro" })
    };

    /// <summary>Guesses a subfolder (e.g. boots/helmets) from the dropped file name.</summary>
    public static string? SuggestSubfolder(string sourcePath, ModTypeConfig type)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath).ToLowerInvariant();
        var available = type.ScanSubfolders
            .Where(subfolder => !string.IsNullOrWhiteSpace(subfolder))
            .ToList();

        foreach (var (folder, hints) in SubfolderHints)
        {
            var match = available.FirstOrDefault(candidate =>
                string.Equals(candidate, folder, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            if (hints.Any(hint => name.Contains(hint, StringComparison.Ordinal))) return match;
        }

        return null;
    }

    private static readonly (string TypeKey, string[] Hints)[] TypeHints =
    {
        ("rider", new[]
        {
            "boot", "instinct", "crossfire", "gaerne", "tech10", "tech 10",
            "helmet", "goggle", "mx9", "protection", "protector", "brace", "chest",
            "knee", "elbow", "roost", "rider", "jersey", "pants", "gear", "helmetcam",
            "helmet cam", "gopro", "animation", "pitboard", "pit board"
        }),
        ("tyres", new[] { "tyre", "tire", "mousse" }),
        ("tracks", new[]
        {
            "track", "motocross", "supercross", "enduro", "supermoto", "circuit", "mxgp"
        }),
        ("bike-paint", new[] { "paint", "graphic", "decal", "livery" }),
        ("bikes", new[]
        {
            "ktm", "honda", "yamaha", "suzuki", "kawasaki", "husqvarna", "husky",
            "gasgas", "gas gas", "fantic", "sxf", "sx-f", "sx f", "crf", "yzf",
            "rmz", "kxf", "exc", "bike", " 250", " 450", " 125", " 300"
        }),
        ("misc", new[] { "misc" })
    };

    /// <summary>
    /// Guesses the mod type from the dropped file/folder name. Detection from archive contents
    /// is stronger, so this is only used when there is nothing better and never auto-installs.
    /// </summary>
    public string? SuggestTypeKey(string sourcePath, string? fallback = null)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath.TrimEnd(Path.DirectorySeparatorChar))
            .ToLowerInvariant();

        if (name.Length > 0)
        {
            var keys = _config.Config.ModTypes
                .Select(t => t.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (typeKey, hints) in TypeHints)
            {
                if (!keys.Contains(typeKey) && typeKey != "bike-paint") continue;
                if (hints.Any(hint => name.Contains(hint, StringComparison.Ordinal))) return typeKey;
            }
        }

        return fallback;
    }

    /// <summary>Targets a paint archive/file at bikes/&lt;bike&gt;/paints/.</summary>
    public bool FinalizePaint(InstallPlan plan, string bikeFolder)
    {
        plan.Type = ResolveType("bikes");
        plan.NeedsTypeChoice = false;
        plan.IsBikePaint = true;
        plan.PaintBikeFolder = bikeFolder;
        plan.Files.Clear();
        plan.Warnings.Clear();

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

    private static readonly HashSet<string> RiderGearFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "rider", "riders", "boots", "helmets", "helmetcams", "protections", "gloves", "goggles",
        "animations", "fonts"
    };

    private static bool LooksLikeBikePaint(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0) return false;

        var hasPaint = false;
        foreach (var key in keys)
        {
            var extension = Path.GetExtension(key).ToLowerInvariant();
            if (extension is ".pkz" or ".map" or ".trh" or ".amb" or ".tyre") return false;

            // Rider gear packs also contain .pnt files; they are not bike paints.
            if (key.Split('/').Any(segment => RiderGearFolders.Contains(segment))) return false;

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
            if (plan.IsLayoutFolder)
            {
                return await Task.Run(() => InstallFolderLayout(plan, progress, ct), ct);
            }

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

    private InstallResult InstallFolderLayout(InstallPlan plan, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var total = plan.Files.Count;
        var done = 0;
        var backups = 0;
        var errors = new List<string>();

        foreach (var planned in plan.Files)
        {
            ct.ThrowIfCancellationRequested();

            var source = planned.SourcePath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                errors.Add($"{planned.EntryKey}: source file is missing");
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(planned.DestinationPath)!);
                if (File.Exists(planned.DestinationPath)) backups += Backup(planned.DestinationPath);

                File.Copy(source, planned.DestinationPath, overwrite: true);
                done++;
                progress?.Report(new InstallProgress(done, total, planned.EntryKey));
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to install {planned.EntryKey}", ex);
                errors.Add($"{planned.EntryKey}: {ex.Message}");
            }
        }

        var success = errors.Count == 0 && done > 0;
        _log.Info($"Installed folder layout {plan.SourceName}: {done}/{total} files, {backups} backed up");

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

    private static string? DetectTypeKey(IEnumerable<string> keys)
    {
        var extensions = keys
            .Select(k => Path.GetExtension(k).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (extensions.Contains(".map") || extensions.Contains(".trh") || extensions.Contains(".amb")) return "tracks";
        if (extensions.Contains(".tyre")) return "tyres";
        if (extensions.Contains(".pnt") || extensions.Contains(".scl") || extensions.Contains(".skn"))
        {
            // .pnt is used by both bike paints and rider gear paints.
            if (keys.Any(key => key.Split('/').Any(segment => RiderGearFolders.Contains(segment))))
            {
                return "rider";
            }
            return "bikes";
        }
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

    private sealed record LayoutMatch(ModTypeConfig Type, string Prefix, string? Subfolder);

    /// <summary>
    /// Finds a game-layout tree anywhere near the root: mods/rider/..., MX Bikes/mods/rider/...,
    /// DownloadPack/mods/rider/..., or rider/.... Root-level stray files are ignored.
    /// </summary>
    private LayoutMatch? DetectLayout(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return null;

        var candidates = new Dictionary<string, (ModTypeConfig Type, int Index, int Matches)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            var segments = key.Split('/');
            for (var i = 0; i < segments.Length - 1 && i <= 3; i++)
            {
                var type = ResolveType(segments[i]);
                if (type is null) continue;

                var prefixSegments = segments.Take(i + 1).ToArray();
                var prefix = string.Join('/', prefixSegments);

                var matches = 0;
                var conflict = false;

                foreach (var other in keys)
                {
                    var otherSegments = other.Split('/');
                    if (otherSegments.Length <= i + 1) continue;

                    if (!otherSegments.Take(i + 1).SequenceEqual(prefixSegments, StringComparer.OrdinalIgnoreCase))
                    {
                        conflict = true;
                        break;
                    }

                    matches++;
                }

                if (conflict || matches == 0) continue;

                if (!candidates.TryGetValue(prefix, out var existing) || matches > existing.Matches)
                {
                    candidates[prefix] = (type, i, matches);
                }
            }
        }

        if (candidates.Count == 0) return null;

        var best = candidates
            .OrderByDescending(candidate => candidate.Value.Matches)
            .ThenBy(candidate => candidate.Value.Index)
            .First();

        var subfolder = DetectLayoutSubfolder(keys, best.Key, best.Value.Type);
        return new LayoutMatch(best.Value.Type, best.Key, subfolder);
    }

    private static string? DetectLayoutSubfolder(IReadOnlyList<string> keys, string prefix, ModTypeConfig type)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subfolder in type.ScanSubfolders)
        {
            if (!string.IsNullOrWhiteSpace(subfolder)) known.Add(subfolder);
        }
        if (!string.IsNullOrWhiteSpace(type.InstallSubfolder)) known.Add(type.InstallSubfolder);
        if (known.Count == 0) return null;

        string? found = null;

        foreach (var key in keys)
        {
            var rest = RelativeAfterPrefix(key, prefix);
            if (rest is null) continue;

            var slash = rest.IndexOf('/');
            if (slash <= 0) return null;

            var first = rest[..slash];
            if (!known.Contains(first)) return null;

            if (found is null) found = first;
            else if (!string.Equals(found, first, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return found;
    }

    private void ApplyLayout(
        InstallPlan plan,
        LayoutMatch layout,
        string sourceRoot,
        IReadOnlyList<string> keys,
        bool isFolder)
    {
        plan.Type = layout.Type;
        plan.DetectedTypeKey = layout.Type.Key;
        plan.HasGameLayout = true;
        plan.LayoutTypeKey = layout.Type.Key;
        plan.LayoutSubfolder = layout.Subfolder;
        plan.NeedsTypeChoice = false;
        plan.IsLayoutFolder = isFolder;
        plan.DetectedKind = isFolder
            ? $"folder · game layout ({DescribeLayout(layout)})"
            : $"game layout ({DescribeLayout(layout)})";

        plan.Entries.Clear();

        foreach (var key in keys)
        {
            var rest = RelativeAfterPrefix(key, layout.Prefix);
            if (rest is null) continue;

            var collapsed = CollapseLayoutRest(rest, layout.Type, layout.Subfolder);
            if (collapsed.Length == 0) continue;

            plan.Entries.Add(new ArchiveEntry
            {
                RawKey = isFolder ? rest : key,
                RelativeKey = collapsed,
                SourcePath = isFolder
                    ? Path.Combine(sourceRoot, key.Replace('/', Path.DirectorySeparatorChar))
                    : null
            });
        }

        BuildLayoutFiles(plan, layout.Type, preferredSubfolder: null);
    }

    /// <summary>Builds final destinations for an already-normalised game-layout plan.</summary>
    private void BuildLayoutFiles(InstallPlan plan, ModTypeConfig type, string? preferredSubfolder)
    {
        plan.Type = type;
        plan.NeedsTypeChoice = false;
        plan.Files.Clear();

        var sameLayoutType = string.Equals(type.Key, plan.LayoutTypeKey, StringComparison.OrdinalIgnoreCase);

        var subfolder = !string.IsNullOrWhiteSpace(preferredSubfolder)
            ? preferredSubfolder.Trim()
            : sameLayoutType
                ? plan.LayoutSubfolder
                : type.InstallSubfolder;

        var typeRoot = Path.Combine(ModsRoot, type.Folder);
        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            typeRoot = Path.Combine(typeRoot, subfolder);
        }

        foreach (var entry in plan.Entries)
        {
            if (HasTraversal(entry.RelativeKey))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }

            var relative = entry.RelativeKey;
            if (!string.IsNullOrWhiteSpace(plan.LayoutSubfolder) &&
                relative.StartsWith(plan.LayoutSubfolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[(plan.LayoutSubfolder.Length + 1)..];
            }

            if (relative.Length == 0) continue;

            var destination = Path.Combine(typeRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!IsInsideModsRoot(destination))
            {
                plan.Warnings.Add($"Skipped unsafe path: {entry.RelativeKey}");
                continue;
            }

            plan.Files.Add(new PlannedFile
            {
                EntryKey = entry.RawKey,
                SourcePath = entry.SourcePath,
                DestinationPath = destination
            });
        }

        CountOverwrites(plan);
    }

    private string CollapseLayoutRest(string rest, ModTypeConfig type, string? subfolder)
    {
        var segments = rest.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        // mods/rider/rider/... → drop the repeated type folder.
        while (segments.Count > 1)
        {
            var firstType = ResolveType(segments[0]);
            if (firstType is null ||
                !string.Equals(firstType.Key, type.Key, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            segments.RemoveAt(0);
        }

        // mods/rider/riders/riders/... → drop the repeated subfolder.
        if (!string.IsNullOrWhiteSpace(subfolder))
        {
            while (segments.Count > 1 &&
                   string.Equals(segments[0], subfolder, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(segments[1], subfolder, StringComparison.OrdinalIgnoreCase))
            {
                segments.RemoveAt(0);
            }
        }

        return string.Join('/', segments);
    }

    private static bool IsLayoutDominant(IReadOnlyList<string> keys, string prefix)
    {
        var deepKeys = keys.Where(key => key.Contains('/')).ToList();
        if (deepKeys.Count == 0) return false;

        // Every deep file must belong to the detected tree; root-level stray files are ignored.
        return deepKeys.All(key => RelativeAfterPrefix(key, prefix) is not null);
    }

    private static string? RelativeAfterPrefix(string key, string prefix)
    {
        if (!key.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return null;
        return key[(prefix.Length + 1)..];
    }

    private static string DescribeLayout(LayoutMatch layout) =>
        string.IsNullOrWhiteSpace(layout.Subfolder)
            ? layout.Type.Folder
            : $"{layout.Type.Folder}/{layout.Subfolder}";

    private static List<string> EnumerateFolderKeys(string root, int? limit = null)
    {
        var keys = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                var relative = Path.GetRelativePath(root, file);
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    continue;
                }
                keys.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
                if (limit is not null && keys.Count >= limit.Value) break;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return keys;
    }

    private List<ArchiveEntry> MapEntries(List<string> rawKeys)
    {
        return rawKeys.Select(raw => new ArchiveEntry
        {
            RawKey = raw,
            RelativeKey = StripKnownWrappers(raw)
        }).ToList();
    }

    private static string StripKnownWrappers(string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (segments.Count > 1 &&
               WrapperNames.Any(wrapper => string.Equals(wrapper, segments[0], StringComparison.OrdinalIgnoreCase)))
        {
            segments.RemoveAt(0);
        }

        return string.Join('/', segments);
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
