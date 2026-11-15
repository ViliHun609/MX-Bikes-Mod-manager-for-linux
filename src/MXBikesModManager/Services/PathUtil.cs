namespace MXBikesModManager.Services;

/// <summary>Case-insensitive path helpers, because Proton prefix folders vary in casing.</summary>
public static class PathUtil
{
    public static string? FindChildIgnoreCase(string parent, string name)
    {
        if (!Directory.Exists(parent)) return null;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent))
            {
                if (string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }
            foreach (var file in Directory.EnumerateFiles(parent))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Walks a relative path segment by segment, ignoring casing. Returns null if any segment is missing.</summary>
    public static string? ResolveIgnoreCase(string root, params string[] segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            var next = FindChildIgnoreCase(current, segment);
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    public static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static IEnumerable<string> EnumerateFilesSafe(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static long DirectorySize(string path, CancellationToken ct = default)
    {
        long total = 0;
        try
        {
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                foreach (var file in EnumerateFilesSafe(dir, "*"))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { }
                }
                foreach (var sub in EnumerateDirectoriesSafe(dir))
                {
                    stack.Push(sub);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Size is best effort only.
        }
        return total;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit <= 1 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
