using System.Text.Json;
using MXBikesModManager.Models;

namespace MXBikesModManager.Services;

/// <summary>
/// Caches mod metadata/preview pointers between runs. Keyed by identity path, file size
/// and last-write time so edits and replacements are picked up automatically.
/// </summary>
public sealed class MetadataCache
{
    private readonly string _file;
    private readonly LogService _log;
    private readonly object _gate = new();
    private readonly HashSet<string> _usedKeys = new(StringComparer.Ordinal);
    private Dictionary<string, CachedEntry> _entries = new(StringComparer.Ordinal);
    private bool _dirty;

    public MetadataCache(string cacheDirectory, LogService log)
    {
        _log = log;
        Directory.CreateDirectory(cacheDirectory);
        _file = Path.Combine(cacheDirectory, "modmeta.json");
        Load();
    }

    public bool TryGet(string identityPath, long size, DateTime lastWrite, out ModInfo? info, out PreviewSource? preview)
    {
        var key = MakeKey(identityPath, size, lastWrite);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _usedKeys.Add(key);
                info = entry.Info;
                preview = entry.Preview switch
                {
                    null => null,
                    { ArchivePath: not null, EntryName: not null } p => PreviewSource.FromArchive(p.ArchivePath, p.EntryName),
                    { FilePath: not null } p => PreviewSource.FromFile(p.FilePath),
                    _ => null
                };
                return true;
            }
        }

        info = null;
        preview = null;
        return false;
    }

    public void Put(string identityPath, long size, DateTime lastWrite, ModInfo? info, PreviewSource? preview)
    {
        var key = MakeKey(identityPath, size, lastWrite);
        lock (_gate)
        {
            _entries[key] = new CachedEntry
            {
                Info = info,
                Preview = preview is null
                    ? null
                    : new CachedPreview
                    {
                        ArchivePath = preview.ArchivePath,
                        EntryName = preview.EntryName,
                        FilePath = preview.FilePath
                    }
            };
            _usedKeys.Add(key);
            _dirty = true;
        }
    }

    public void SaveIfDirty()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;

            // A scan touches every mod it still finds, so anything else is stale (removed,
            // renamed or replaced mods). Drop it instead of letting the cache grow forever.
            if (_usedKeys.Count > 0)
            {
                foreach (var stale in _entries.Keys.Where(key => !_usedKeys.Contains(key)).ToList())
                {
                    _entries.Remove(stale);
                }
                _usedKeys.Clear();
            }

            try
            {
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(_file, json);
            }
            catch (Exception ex)
            {
                _log.Error("Could not save metadata cache", ex);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var json = File.ReadAllText(_file);
            var data = JsonSerializer.Deserialize<Dictionary<string, CachedEntry>>(json);
            if (data is not null)
            {
                _entries = new Dictionary<string, CachedEntry>(data, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Ignoring unreadable metadata cache: {ex.Message}");
            _entries = new Dictionary<string, CachedEntry>(StringComparer.Ordinal);
        }
    }

    private static string MakeKey(string path, long size, DateTime lastWrite)
        => $"{path}\u0001{size}\u0001{lastWrite.Ticks}";

    private sealed class CachedEntry
    {
        public ModInfo? Info { get; set; }
        public CachedPreview? Preview { get; set; }
    }

    private sealed class CachedPreview
    {
        public string? ArchivePath { get; set; }
        public string? EntryName { get; set; }
        public string? FilePath { get; set; }
    }
}
