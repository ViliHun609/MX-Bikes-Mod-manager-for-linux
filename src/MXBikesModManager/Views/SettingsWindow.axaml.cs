using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MXBikesModManager.Services;
using MXBikesModManager.ViewModels;

namespace MXBikesModManager.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly string _originalTheme = ThemeService.DefaultKey;
    private bool _loadingThemes;
    private bool _saved;

    /// <summary>Designer-only constructor; the app always passes the already-loaded config.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
        _config = null!;
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
        SmoothScrollCheck.IsChecked = config.Config.Ui.SmoothScrolling;
        DataPathText.Text = config.Data.Root;
        Title = $"Settings — {config.Config.Game.Name}";

        _originalTheme = ThemeService.Current.Key;
        _loadingThemes = true;
        var options = ThemeService.Palettes.Select(p => new ThemeOptionViewModel(p)).ToList();
        ThemeList.ItemsSource = options;
        ThemeList.SelectedItem = options.FirstOrDefault(o => o.Key == _originalTheme) ?? options[0];
        _loadingThemes = false;

        // Closing with the title-bar X also restores the theme unless Save was pressed.
        Closed += (_, _) =>
        {
            if (!_saved && !string.Equals(ThemeService.Current.Key, _originalTheme, StringComparison.OrdinalIgnoreCase))
            {
                ThemeService.Apply(_originalTheme);
            }
        };

        if (!Directory.Exists(config.Config.Game.ModsRoot))
        {
            ShowWarning("The current mods folder does not exist. Browse to it or use Auto-detect.");
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingThemes || ThemeList.SelectedItem is not ThemeOptionViewModel option) return;
        if (string.Equals(ThemeService.Current.Key, option.Key, StringComparison.OrdinalIgnoreCase)) return;
        ThemeService.Apply(option.Key);
        HideWarning();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            _config.Log.Error("Folder picker failed", ex);
            ShowWarning($"Could not open the folder picker: {ex.Message}");
        }
    }

    private void OnDetect(object? sender, RoutedEventArgs e)
    {
        var result = SteamLocator.FindModsRoot(_config.Config.Game.AppId);
        if (result.ModsRoot is not null)
        {
            ModsRootBox.Text = result.ModsRoot;
            HideWarning();
            _config.Log.Info($"Auto-detect found mods folder: {result.ModsRoot}");
            return;
        }

        var searched = result.Searched.Count > 0
            ? $"Searched {result.Searched.Count} Proton prefix location(s)."
            : "No Steam library was found.";
        _config.Log.Warn($"Auto-detect failed. {searched} Searched: {string.Join(", ", result.Searched)}");
        ShowWarning(
            $"Could not detect the folder automatically. {searched} " +
            "Start MX Bikes once through Steam/Proton, then try again — or pick the folder with Browse…");
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
        _config.Config.Ui.SmoothScrolling = SmoothScrollCheck.IsChecked == true;
        _config.Config.Ui.Theme = (ThemeList.SelectedItem as ThemeOptionViewModel)?.Key ?? _originalTheme;

        if (!_config.Save())
        {
            ShowWarning($"Could not write {_config.ConfigPath}.");
            return;
        }

        _saved = true;
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
