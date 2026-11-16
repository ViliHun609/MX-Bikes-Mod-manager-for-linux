namespace MXBikesModManager.Models;

public enum ModKind
{
    PkzArchive,
    Folder
}

public sealed class ModInfo
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Location { get; set; }
    public string? Discipline { get; set; }

}

/// <summary>
/// Points at the best available preview image for a mod: either an entry inside a
/// .pkz zip or a plain file on disk.
/// </summary>
public sealed class PreviewSource
{
    public string? ArchivePath { get; init; }
    public string? EntryName { get; init; }
    public string? FilePath { get; init; }

    public bool IsArchive => !string.IsNullOrEmpty(ArchivePath) && !string.IsNullOrEmpty(EntryName);
    public bool IsFile => !string.IsNullOrEmpty(FilePath);

    public static PreviewSource FromArchive(string archivePath, string entryName)
        => new() { ArchivePath = archivePath, EntryName = entryName };

    public static PreviewSource FromFile(string filePath)
        => new() { FilePath = filePath };
}

public sealed class ModEntry
{
    public required string Name { get; init; }
    public required ModTypeConfig Type { get; init; }
    public string Category { get; init; } = string.Empty;
    public required ModKind Kind { get; init; }

    /// <summary>Current on-disk path of the primary item (.pkz file or folder), including the disabled suffix if disabled.</summary>
    public required string Path { get; set; }

    public string? PairedFolderPath { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long SizeBytes { get; set; }
    public bool SizeKnown { get; set; }
    public ModInfo? Info { get; set; }
    public PreviewSource? Preview { get; set; }

    /// <summary>Stable key within a type: category + base name (no extension, no disabled suffix).</summary>
    public string BaseName { get; init; } = string.Empty;

    public string DisplayName => Info?.Name is { Length: > 0 } n ? n : BaseName;
    public string? Subtitle
    {
        get
        {
            if (Info is null) return null;
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Info.Author)) parts.Add(Info.Author!);
            if (!string.IsNullOrWhiteSpace(Info.Location)) parts.Add(Info.Location!);
            if (!string.IsNullOrWhiteSpace(Info.Discipline)) parts.Add(Info.Discipline!);
            return parts.Count == 0 ? null : string.Join(" • ", parts);
        }
    }
}
