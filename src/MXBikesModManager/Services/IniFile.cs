namespace MXBikesModManager.Services;

/// <summary>Minimal INI reader for PiBoSo metadata files (track .ini, bike .cfg inside pkz).</summary>
public static class IniFile
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;
        result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line[0] is ';' or '#') continue;

            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                if (end > 0)
                {
                    currentSection = line[1..end].Trim();
                    if (!result.ContainsKey(currentSection))
                    {
                        result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length == 0) continue;
            result[currentSection][key] = value;
        }

        return result;
    }

    public static string? Get(Dictionary<string, Dictionary<string, string>> ini, string section, string key)
    {
        if (!ini.TryGetValue(section, out var values)) return null;
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
