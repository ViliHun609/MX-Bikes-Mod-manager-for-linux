using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MXBikesModManager.Models;
using MXBikesModManager.Services;
using MXBikesModManager.ViewModels;

namespace MXBikesModManager.Views;

public partial class MainWindow : Window, IUiInteraction
{
    private readonly ConfigService _config;
    private readonly MainWindowViewModel _viewModel;
    private bool _dragLogged;

    public MainWindow() : this(ConfigService.Load(null)) { }

    public MainWindow(ConfigService config, string? initialTab = null)
    {
        _config = config;
        InitializeComponent();

        _viewModel = new MainWindowViewModel(config, this, initialTab);
        DataContext = _viewModel;

        // Belt and braces: the XAML also sets DragDrop.AllowDrop on the window.
        DragDrop.SetAllowDrop(this, true);

        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter, routes, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, routes, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, routes, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, routes, handledEventsToo: true);

        Opened += async (_, _) =>
        {
            _viewModel.Initialize();
            if (_viewModel.HasModsFolder)
            {
                await _viewModel.RefreshAsync();
            }
            else
            {
                await ShowSettingsAsync(_config);
                _viewModel.ApplySettingsChange();
                if (_viewModel.HasModsFolder) await _viewModel.RefreshAsync();
            }
        };
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _dragLogged = false;
        HandleDrag(e, "enter");
    }

    private void OnDragOver(object? sender, DragEventArgs e) => HandleDrag(e, "over");

    private void HandleDrag(DragEventArgs e, string phase)
    {
        var files = e.DataTransfer.TryGetFiles()?.ToList();
        var hasFiles = files is { Count: > 0 };

        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = hasFiles;
        e.Handled = true;

        if (_dragLogged) return;
        _dragLogged = true;

        var formats = e.DataTransfer.Formats.Select(format => format.ToString());
        _config.Log.Info(
            $"Drag {phase}: files={files?.Count ?? 0}, formats=[{string.Join(", ", formats)}]");
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _dragLogged = false;
        DropOverlay.IsVisible = false;
        _config.Log.Info("Drag leave.");
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        _dragLogged = false;
        e.Handled = true;

        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files is null || files.Count == 0)
        {
            var formats = e.DataTransfer.Formats.Select(format => format.ToString());
            _config.Log.Warn($"Drop ignored: no file data. Formats=[{string.Join(", ", formats)}]");
            _viewModel.Status = "That drop did not contain local files. Drag files or folders from your file manager.";
            return;
        }

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        _config.Log.Info(
            $"Drop: {files.Count} item(s), {paths.Count} local path(s): {string.Join(" | ", paths)}");

        if (paths.Count == 0)
        {
            _viewModel.Status = "That drop did not contain local files. Drag files or folders from your file manager.";
            return;
        }

        try
        {
            await _viewModel.InstallPathsAsync(paths);
        }
        catch (Exception ex)
        {
            _config.Log.Error("Drop install failed", ex);
            _viewModel.Status = $"Drop install failed: {ex.Message}";
        }
    }

    public async Task<IReadOnlyList<string>> PickModArchivesAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Install MX Bikes mods",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("MX Bikes mods")
                    {
                        Patterns = new[] { "*.zip", "*.7z", "*.rar", "*.pkz", "*.tar", "*.gz", "*.tgz" }
                    },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });

            return files
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();
        }
        catch (Exception ex)
        {
            _config.Log.Error("File picker failed", ex);
            _viewModel.Status = $"Could not open the file picker: {ex.Message}";
            return Array.Empty<string>();
        }
    }

    public async Task<string?> PickModsFolderAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the MX Bikes mods folder",
                AllowMultiple = false
            });

            return folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        }
        catch (Exception ex)
        {
            _config.Log.Error("Folder picker failed", ex);
            _viewModel.Status = $"Could not open the folder picker: {ex.Message}";
            return null;
        }
    }

    public async Task<InstallChoice?> PickInstallTypeAsync(InstallPlan plan, IReadOnlyList<string> paintBikes)
    {
        var window = new InstallTargetWindow(plan, _config.Config.ModTypes, paintBikes);
        return await window.ShowDialog<InstallChoice?>(this);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue")
    {
        var window = MessageWindow.CreateConfirm(title, message, confirmText);
        return await window.ShowDialog<bool>(this);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = MessageWindow.CreateInfo(title, message);
        await window.ShowDialog<bool>(this);
    }

    public async Task<bool> ShowSettingsAsync(ConfigService config)
    {
        var window = new SettingsWindow(config, this);
        return await window.ShowDialog<bool>(this);
    }
}
