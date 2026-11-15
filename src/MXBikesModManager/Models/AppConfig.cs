using System.Collections.Generic;

namespace MXBikesModManager.Models;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public GameConfig Game { get; set; } = new();
    public DataConfig Data { get; set; } = new();
    public ModsConfig Mods { get; set; } = new();
    public List<ModTypeConfig> ModTypes { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
}

public sealed class GameConfig
{
    public string AppId { get; set; } = "655500";
    public string Name { get; set; } = "MX Bikes";
    public string ModsRoot { get; set; } = string.Empty;
    public string LaunchUri { get; set; } = "steam://rungameid/655500";
    public bool AutoDetect { get; set; } = true;
}

public sealed class DataConfig
{
    public string Root { get; set; } = "data";
    public string Cache { get; set; } = "cache";
    public string Thumbs { get; set; } = "thumbs";
    public string Logs { get; set; } = "logs";
    public string Backups { get; set; } = "backups";
    public string Trash { get; set; } = "trash";
}

public sealed class ModsConfig
{
    public string DisabledSuffix { get; set; } = ".disabled";
    public string DisableStrategy { get; set; } = "renameSuffix";
    public int ScanDepth { get; set; } = 2;
    public bool ComputeSizes { get; set; } = true;
}

public sealed class ModTypeConfig
{
    public string Key { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public bool PairedFolder { get; set; }
    public string InstallSubfolder { get; set; } = string.Empty;
    public List<string> ScanSubfolders { get; set; } = new();
    public bool CategorizeBySubfolder { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Folder : Title;
    public IEnumerable<string> SubfoldersToScan =>
        ScanSubfolders.Count > 0 ? ScanSubfolders : new[] { string.Empty };
}

public sealed class UiConfig
{
    public string Layout { get; set; } = "grid";
    public double TileWidth { get; set; } = 230;
    public double TileHeight { get; set; } = 200;
    public bool ShowPreviews { get; set; } = true;
    public string DefaultSort { get; set; } = "name";
    public bool HideDisabledByDefault { get; set; }
    public bool RememberWindow { get; set; } = true;
}
