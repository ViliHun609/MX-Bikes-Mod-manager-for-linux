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

    public MainWindow() : this(ConfigService.Load(null)) { }

    public MainWindow(ConfigService config, string? initialTab = null)
    {
        _config = config;
        InitializeComponent();

        _viewModel = new MainWindowViewModel(config, this, initialTab);
        DataContext = _viewModel;

        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = hasFiles;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        e.Handled = true;

        if (paths.Count > 0)
        {
            await _viewModel.InstallPathsAsync(paths);
        }
    }

    public async Task<IReadOnlyList<string>> PickModArchivesAsync()
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

    public async Task<string?> PickModsFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the MX Bikes mods folder",
            AllowMultiple = false
        });

        return folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
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
