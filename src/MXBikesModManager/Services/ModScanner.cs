using System.Text;
using System.Text.RegularExpressions;
using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

public sealed class ScanResult
{
    public List<ModEntry> Entries { get; } = new();
    public List<string> Problems { get; } = new();
    public int WithMetadata { get; set; }
    public int WithPreview { get; set; }
}

/// <summary>
/// Enumerates a type folder (bikes/tracks/tyres/rider/misc). A mod is either a .pkz
/// archive, a loose folder, or a .pkz plus a matching folder holding paints/assets.
/// </summary>
public sealed class ModScanner
{
    private static readonly Dictionary<string, string> CategoryTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["helmetcams"] = "Helmet Cams",
        ["riders"] = "Riders",
        ["motocross"] = "Motocross",
        ["supercross"] = "Supercross",
        ["supermoto"] = "Supermoto",
        ["enduro"] = "Enduro",
        ["pitboard"] = "Pit Board",
        ["fonts"] = "Fonts",
        ["Mods"] = "Mods"
    };

    private readonly ConfigService _config;
    private readonly MetadataCache _cache;
    private readonly PkzInspector _inspector = new();
    private readonly LogService _log;

    public ModScanner(ConfigService config, MetadataCache cache)
    {
        _config = config;
        _cache = cache;
        _log = config.Log;
    }

    public Task<ScanResult> ScanTypeAsync(ModTypeConfig type, CancellationToken ct)
        => Task.Run(() => ScanType(type, ct), ct);

    private ScanResult ScanType(ModTypeConfig type, CancellationToken ct)
    {
        var result = new ScanResult();
        var suffix = _config.Config.Mods.DisabledSuffix;
        var computeSizes = _config.Config.Mods.ComputeSizes;
        var typeDir = Path.Combine(_config.Config.Game.ModsRoot, type.Folder);

        if (!Directory.Exists(typeDir))
        {
            result.Problems.Add($"{type.DisplayTitle}: folder not found — {typeDir}");
            return result;
        }

        var entries = new List<ModEntry>();
        var byKey = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        var categoryRules = CompileCategoryRules(type);

        foreach (var sub in type.SubfoldersToScan)
        {
            ct.ThrowIfCancellationRequested();

            var root = string.IsNullOrEmpty(sub) ? typeDir : Path.Combine(typeDir, sub);
            if (!Directory.Exists(root)) continue;

            var subfolderCategory = type.CategorizeBySubfolder && !string.IsNullOrEmpty(sub)
                ? PrettyCategory(sub)
                : string.Empty;

            // 1. .pkz archives (enabled and disabled)
            foreach (var file in PathUtil.EnumerateFilesSafe(root, "*"))
            {
                if (!TryParseArchiveName(Path.GetFileName(file), suffix, out var baseName, out var disabled)) continue;

                var category = Categorize(baseName, subfolderCategory, type.DefaultCategory, categoryRules);

                var entry = new ModEntry
                {
                    Name = baseName,
                    BaseName = baseName,
                    Type = type,
                    Kind = ModKind.PkzArchive,
                    Category = category,
                    Path = file,
                    IsEnabled = !disabled,
                    SizeBytes = SafeLength(file),
                    SizeKnown = true
                };

                entries.Add(entry);
                byKey[Key(category, baseName)] = entry;
            }

            // 2. Loose folders, pairing with a .pkz when the names match
            foreach (var dir in PathUtil.EnumerateDirectoriesSafe(root))
            {
                ct.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;
                if (!TryParseFolderName(dirName, suffix, out var baseName, out var disabled)) continue;

                var category = Categorize(baseName, subfolderCategory, type.DefaultCategory, categoryRules);
                var key = Key(category, baseName);
                if (type.PairedFolder && byKey.TryGetValue(key, out var paired))
                {
                    if (paired.Kind == ModKind.PkzArchive && paired.PairedFolderPath is null)
                    {
                        paired.PairedFolderPath = dir;
                        continue;
                    }
                }

                var entry = new ModEntry
                {
                    Name = baseName,
                    BaseName = baseName,
                    Type = type,
                    Kind = ModKind.Folder,
                    Category = category,
                    Path = dir,
                    IsEnabled = !disabled,
                    SizeKnown = false
                };

                entries.Add(entry);
                byKey[key] = entry;
            }
        }

        // 3. Metadata and previews (cached between runs)
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            Enrich(entry, useCache: true);
        }

        // 4. Sizes of paired folders, in parallel; this is pure I/O and can be slow on 8 GB of paints.
        if (computeSizes)
        {
            var folderSizeTargets = entries
                .Where(e => e.PairedFolderPath is not null || e.Kind == ModKind.Folder)
                .ToList();

            Parallel.ForEach(
                folderSizeTargets,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                entry =>
                {
                    var folder = entry.PairedFolderPath ?? entry.Path;
                    if (entry.Kind == ModKind.Folder && !entry.IsEnabled) { /* still fine */ }
                    var size = PathUtil.DirectorySize(folder, ct);
                    entry.SizeBytes += size;
                    entry.SizeKnown = true;
                });
        }

        result.Entries.AddRange(entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase));
        result.WithMetadata = entries.Count(e => e.Info is { IsEmpty: false });
        result.WithPreview = entries.Count(e => e.Preview is not null);
        return result;
    }

    private List<(string Name, Regex Pattern)> CompileCategoryRules(ModTypeConfig type)
    {
        var rules = new List<(string Name, Regex Pattern)>();
        foreach (var rule in type.CategoryRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                rules.Add((rule.Name.Trim(), new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
            }
            catch (ArgumentException ex)
            {
                _log.Warn($"Ignoring invalid category pattern '{rule.Pattern}': {ex.Message}");
            }
        }
        return rules;
    }

    private static string Categorize(
        string baseName,
        string subfolderCategory,
        string defaultCategory,
        List<(string Name, Regex Pattern)> rules)
    {
        if (subfolderCategory.Length > 0) return subfolderCategory;

        foreach (var (name, pattern) in rules)
        {
            if (pattern.IsMatch(baseName)) return name;
        }

        return defaultCategory;
    }

    private void Enrich(ModEntry entry, bool useCache)
    {
        // Cache identity ignores the disabled suffix, so toggling does not invalidate metadata.
        var identityPath = IdentityPath(entry);
        var size = SafeLength(entry.Path);
        var lastWrite = SafeLastWrite(entry.Path);

        if (useCache && _cache.TryGet(identityPath, size, lastWrite, out var cachedInfo, out var cachedPreview))
        {
            var previewStillValid = cachedPreview is not { IsFile: true } || File.Exists(cachedPreview.FilePath);
            if (previewStillValid)
            {
                entry.Info = cachedInfo;
                entry.Preview = cachedPreview;
                return;
            }
        }

        ModInfo? info = null;
        PreviewSource? preview = null;

        if (entry.Kind == ModKind.PkzArchive)
        {
            var inspection = _inspector.Inspect(entry.Path);
            info = inspection.Info;
            preview = inspection.Preview;
        }
        else
        {
            info = ReadFolderInfo(entry.Path);
            preview = PkzInspector.FindFolderPreview(entry.Path);
        }

        entry.Info = info;
        entry.Preview = preview;
        _cache.Put(identityPath, size, lastWrite, info, preview);
    }

    private static ModInfo? ReadFolderInfo(string folder)
    {
        try
        {
            foreach (var file in PathUtil.EnumerateFilesSafe(folder, "*.ini").Take(4))
            {
                var text = File.ReadAllText(file);
                if (text.Length > 64 * 1024) text = text[..(64 * 1024)];
                var ini = IniFile.Parse(text);
                var name = IniFile.Get(ini, "info", "name");
                var author = IniFile.Get(ini, "ui", "author");
                var location = IniFile.Get(ini, "ui", "location");
                if (name is null && author is null && location is null) continue;

                return new ModInfo
                {
                    Name = name,
                    ShortName = IniFile.Get(ini, "info", "short_name"),
                    Author = author,
                    Location = location
                };
            }
        }
        catch (Exception)
        {
            // Metadata is optional.
        }

        return null;
    }

    private string IdentityPath(ModEntry entry)
    {
        // Cache identity ignores the disabled suffix, so toggling does not invalidate metadata.
        var path = entry.Path;
        var suffix = _config.Config.Mods.DisabledSuffix;
        if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) path = path[..^suffix.Length];
        return path;
    }

    private static string Key(string category, string baseName) => $"{category}\u0001{baseName}";

    public static string PrettyCategory(string folder)
    {
        if (CategoryTitles.TryGetValue(folder, out var title)) return title;
        if (folder.Length == 0) return folder;
        var builder = new StringBuilder(folder.Length);
        var upperNext = true;
        foreach (var ch in folder)
        {
            if (ch is '_' or '-')
            {
                builder.Append(' ');
                upperNext = true;
                continue;
            }
            builder.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
            upperNext = false;
        }
        return builder.ToString();
    }

    public static bool TryParseArchiveName(string fileName, string suffix, out string baseName, out bool disabled)
    {
        baseName = string.Empty;
        disabled = false;

        var name = fileName;
        if (suffix.Length > 0 && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
        {
            disabled = true;
            name = name[..^suffix.Length];
        }

        if (!name.EndsWith(".pkz", StringComparison.OrdinalIgnoreCase) || name.Length <= 4) return false;
        baseName = name[..^4];
        return true;
    }

    public static bool TryParseFolderName(string dirName, string suffix, out string baseName, out bool disabled)
    {
        baseName = dirName;
        disabled = false;
        if (suffix.Length > 0 && dirName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && dirName.Length > suffix.Length)
        {
            disabled = true;
            baseName = dirName[..^suffix.Length];
        }
        return baseName.Length > 0;
    }

    private static long SafeLength(string path)
    {
        try
        {
            if (Directory.Exists(path)) return 0;
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static DateTime SafeLastWrite(string path)
    {
        try
        {
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
            return new FileInfo(path).LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }
}
