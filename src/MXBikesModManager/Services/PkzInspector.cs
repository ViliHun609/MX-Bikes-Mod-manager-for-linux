using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

/// <summary>
/// Reads metadata and preview images out of a .pkz file.
/// Most mods are plain ZIP archives (tracks, some tyres); the rest are PiBoSo's
/// obfuscated format, in which case inspection simply returns nothing.
/// </summary>
public sealed class PkzInspector
{
    private static readonly string[] ImageExtensions = { ".tga", ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    private static readonly string[] PreviewHints = { "capa", "preview", "cover", "thumb", "banner", "logo", "screenshot", "shot", "pic" };
    private const long MaxPreviewBytes = 24L * 1024 * 1024;

    public PkzInspection Inspect(string archivePath, CancellationToken ct = default)
    {
        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

            var entries = zip.Entries
                .Where(e => e.Name.Length > 0 && !e.FullName.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var info = ReadInfo(entries);
            var preview = PickPreview(archivePath, entries, info);
            return new PkzInspection(info, preview);
        }
        catch (Exception)
        {
            // Not a readable zip (obfuscated pkz) or unreadable file: no metadata, no preview.
            return PkzInspection.Empty;
        }
    }

    private static ModInfo? ReadInfo(IEnumerable<System.IO.Compression.ZipArchiveEntry> entries)
    {
        var iniEntries = entries
            .Where(e => e.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                        || e.Name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(c => c == '/'))
            .Take(12)
            .ToList();

        foreach (var entry in iniEntries)
        {
            string text;
            try
            {
                // Only read a bounded prefix: entry.Length is attacker-controlled and a
                // "zip bomb" .ini must not be able to allocate unbounded memory.
                using var reader = new StreamReader(entry.Open());
                var buffer = new char[64 * 1024];
                var read = reader.ReadBlock(buffer, 0, buffer.Length);
                text = new string(buffer, 0, read);
            }
            catch (Exception)
            {
                continue;
            }

            var ini = IniFile.Parse(text);
            var name = IniFile.Get(ini, "info", "name");
            var author = IniFile.Get(ini, "ui", "author");
            var location = IniFile.Get(ini, "ui", "location");
            var discipline = IniFile.Get(ini, "ui", "discipline") ?? IniFile.Get(ini, "info", "discipline")
                             ?? IniFile.Get(ini, "info", "category");

            if (name is null && author is null && location is null) continue;

            return new ModInfo
            {
                Name = name,
                Author = author,
                Location = location,
                Discipline = discipline
            };
        }

        return null;
    }

    private static PreviewSource? PickPreview(
        string archivePath,
        List<System.IO.Compression.ZipArchiveEntry> entries,
        ModInfo? info)
    {
        var images = entries
            .Where(e => ImageExtensions.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase)
                        && e.Length is > 0 and < MaxPreviewBytes)
            .OrderBy(e => e.FullName.Count(c => c == '/'))
            .ToList();

        if (images.Count == 0) return null;

        var preferred = images.FirstOrDefault(e => PreviewHints.Any(h =>
            Path.GetFileNameWithoutExtension(e.Name).Contains(h, StringComparison.OrdinalIgnoreCase)));
        if (preferred is not null) return PreviewSource.FromArchive(archivePath, preferred.FullName);

        return PreviewSource.FromArchive(archivePath, images[0].FullName);
    }

    /// <summary>Looks for a preview image inside a loose mod folder (rare, but some mods ship one).</summary>
    public static PreviewSource? FindFolderPreview(string folder, int maxDepth = 3)
    {
        try
        {
            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((folder, 0));
            var candidates = new List<FileInfo>();

            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                foreach (var file in PathUtil.EnumerateFilesSafe(dir, "*"))
                {
                    var ext = Path.GetExtension(file);
                    if (!ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                    var info = new FileInfo(file);
                    if (info.Length is <= 0 or > MaxPreviewBytes) continue;
                    candidates.Add(info);
                }

                if (depth + 1 > maxDepth) continue;
                foreach (var sub in PathUtil.EnumerateDirectoriesSafe(dir))
                {
                    stack.Push((sub, depth + 1));
                }
            }

            if (candidates.Count == 0) return null;

            var preferred = candidates.FirstOrDefault(f => PreviewHints.Any(h =>
                Path.GetFileNameWithoutExtension(f.Name).Contains(h, StringComparison.OrdinalIgnoreCase)));
            return PreviewSource.FromFile((preferred ?? candidates[0]).FullName);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed record PkzInspection(ModInfo? Info, PreviewSource? Preview)
{
    public static readonly PkzInspection Empty = new(null, null);
}
