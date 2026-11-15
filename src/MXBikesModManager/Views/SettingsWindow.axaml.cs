using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MXBikesModManager.Services;

namespace MXBikesModManager.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = ConfigService.Load(null);
    }

    public SettingsWindow(ConfigService config, Window owner) : this(config)
    {
        Owner = owner;
    }

    public SettingsWindow(ConfigService config) : this()
    {
        _config = config;
        ModsRootBox.Text = config.Config.Game.ModsRoot;
        AutoDetectCheck.IsChecked = config.Config.Game.AutoDetect;
        ShowPreviewsCheck.IsChecked = config.Config.Ui.ShowPreviews;
        HideDisabledCheck.IsChecked = config.Config.Ui.HideDisabledByDefault;
        DataPathText.Text = config.Data.Root;
        Title = $"Settings — {config.Config.Game.Name}";

        if (!Directory.Exists(config.Config.Game.ModsRoot))
        {
            ShowWarning("The current mods folder does not exist. Browse to it or use Auto-detect.");
        }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the MX Bikes mods folder",
            AllowMultiple = false
        });

        var path = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (!string.IsNullOrWhiteSpace(path))
        {
            ModsRootBox.Text = path;
            HideWarning();
        }
    }

    private void OnDetect(object? sender, RoutedEventArgs e)
    {
        var result = SteamLocator.FindModsRoot(_config.Config.Game.AppId);
        if (result.ModsRoot is not null)
        {
            ModsRootBox.Text = result.ModsRoot;
            HideWarning();
            return;
        }

        var searched = result.Searched.Count > 0
            ? $"Searched {result.Searched.Count} prefix locations."
            : "No Steam library found.";
        ShowWarning($"Could not detect the folder automatically. {searched} Pick it with Browse… instead.");
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var path = ModsRootBox.Text?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            ShowWarning("Please choose the mods folder first.");
            return;
        }

        if (!Directory.Exists(path))
        {
            ShowWarning("That folder does not exist on disk. Check the path — the game must have been started at least once so the Proton prefix exists.");
            return;
        }

        var expected = new[] { "bikes", "tracks", "tyres", "rider", "misc" };
        var found = expected.Count(name => PathUtil.FindChildIgnoreCase(path, name) is not null);
        if (found == 0)
        {
            ShowWarning("That folder does not contain the MX Bikes mod folders (bikes, tracks, tyres, rider, misc). Pick …/PiBoSo/MX Bikes/mods.");
            return;
        }

        _config.Config.Game.ModsRoot = path;
        _config.Config.Game.AutoDetect = AutoDetectCheck.IsChecked == true;
        _config.Config.Ui.ShowPreviews = ShowPreviewsCheck.IsChecked == true;
        _config.Config.Ui.HideDisabledByDefault = HideDisabledCheck.IsChecked == true;

        if (!_config.Save())
        {
            ShowWarning($"Could not write {_config.ConfigPath}.");
            return;
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowWarning(string message)
    {
        WarningText.Text = message;
        WarningText.IsVisible = true;
    }

    private void HideWarning() => WarningText.IsVisible = false;
}
