using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ModScanner _scanner;
    private readonly ToggleService _toggle;
    private readonly InstallService _installer;
    private readonly ThumbnailService _thumbs;
    private readonly MetadataCache _cache;
    private readonly LogService _log;
    private readonly SemaphoreSlim _previewGate = new(4);
    private CancellationTokenSource? _scanCts;

    public MainWindowViewModel(ConfigService config, IUiInteraction interaction, string? initialTab = null)
    {
        _config = config;
        _log = config.Log;
        _cache = new MetadataCache(config.Data.Cache, _log);
        _scanner = new ModScanner(config, _cache);
        _toggle = new ToggleService(config);
        _installer = new InstallService(config);
        _thumbs = new ThumbnailService(_log);
        Interaction = interaction;

        _modsRoot = config.Config.Game.ModsRoot;
        _status = config.DetectionNote ?? "Ready.";

        foreach (var type in config.Config.ModTypes)
        {
            Types.Add(CreateTypeViewModel(type));
        }

        _selectedType = Types.FirstOrDefault(t =>
                            string.Equals(t.Type.Key, initialTab, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.Type.Folder, initialTab, StringComparison.OrdinalIgnoreCase))
                        ?? Types.FirstOrDefault();
    }

    public IUiInteraction Interaction { get; }

    public ObservableCollection<ModTypeViewModel> Types { get; } = new();

    public ConfigService Config => _config;

    [ObservableProperty]
    private ModTypeViewModel? _selectedType;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _modsRoot;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasModsFolder;

    public string GameName => _config.Config.Game.Name;
    public string DataFolder => _config.Data.Root;

    private ModTypeViewModel CreateTypeViewModel(ModTypeConfig type)
    {
        return new ModTypeViewModel(
            type,
            toggleHandler: HandleToggle,
            removeHandler: HandleRemove,
            openInFileManager: OpenPath,
            copyPath: CopyToClipboard,
            status: text => Status = text)
        {
            Owner = this,
            HideDisabled = _config.Config.Ui.HideDisabledByDefault
        };
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            var window = (Avalonia.Application.Current?.ApplicationLifetime
                          as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window?.Clipboard is not null)
            {
                _ = window.Clipboard.SetTextAsync(text);
                Status = "Path copied to clipboard.";
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Clipboard failed: {ex.Message}");
        }
    }

    private bool HandleToggle(ModItemViewModel item, bool enabled)
    {
        var result = _toggle.SetEnabled(item.Entry, enabled);
        Status = result.Message;
        if (!result.Success)
        {
            item.RevertEnabled(!enabled);
            return false;
        }

        item.RefreshDetails();
        foreach (var typeVm in Types)
        {
            if (typeVm.AllItems.Contains(item)) typeVm.RecomputeCounts();
        }
        UpdateSummary();
        return true;
    }

    private bool HandleRemove(ModItemViewModel item)
    {
        var result = _toggle.MoveToTrash(item.Entry);
        Status = result.Message;
        if (!result.Success) return false;
        UpdateSummary();
        return true;
    }

    public async Task RefreshAsync()
    {
        if (!HasModsFolder)
        {
            Status = "Mods folder not found — open Settings and pick it.";
            return;
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        IsBusy = true;
        Status = "Scanning mods…";
        var started = DateTime.UtcNow;

        try
        {
            var total = 0;
            var withPreview = 0;
            var problems = new List<string>();

            for (var i = 0; i < Types.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var typeVm = Types[i];
                typeVm.IsScanning = true;
                Status = $"Scanning {typeVm.Title} ({i + 1}/{Types.Count})…";
                var result = await _scanner.ScanTypeAsync(typeVm.Type, ct);
                typeVm.Load(result.Entries);
                typeVm.IsScanning = false;
                total += result.Entries.Count;
                withPreview += result.WithPreview;
                problems.AddRange(result.Problems);
            }

            _cache.SaveIfDirty();
            UpdateSummary();
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            Status = problems.Count > 0
                ? $"Scanned {total} mods in {elapsed:0.0}s. " + problems[0]
                : $"Scanned {total} mods in {elapsed:0.0}s ({withPreview} with preview images).";

            IsBusy = false;
            _ = LoadPreviewsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            IsBusy = false;
            Status = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            IsBusy = false;
            _log.Error("Scan failed", ex);
            Status = $"Scan failed: {ex.Message}";
            await Interaction.ShowMessageAsync("Scan failed", ex.Message);
        }
    }

    private async Task LoadPreviewsAsync(CancellationToken ct)
    {
        if (!_config.Config.Ui.ShowPreviews) return;

        var items = Types.SelectMany(t => t.AllItems)
            .Where(i => i.Entry.Preview is not null && i.PreviewImage is null)
            .ToList();

        if (items.Count == 0) return;

        var tasks = items.Select(async item =>
        {
            await _previewGate.WaitAsync(ct);
            try
            {
                var bitmap = await _thumbs.LoadAsync(item.Entry.Preview!, ct);
                if (bitmap is not null)
                {
                    item.PreviewImage = bitmap;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.Warn($"Preview failed for {item.Entry.DisplayName}: {ex.Message}");
            }
            finally
            {
                _previewGate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateSummary()
    {
        var all = Types.SelectMany(t => t.AllItems).ToList();
        var enabled = all.Count(i => i.IsEnabled);
        var disabled = all.Count - enabled;
        Summary = $"{all.Count} mods • {enabled} enabled • {disabled} disabled";
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private async Task Install()
    {
        var files = await Interaction.PickModArchivesAsync();
        if (files.Count > 0)
        {
            await InstallPathsAsync(files);
        }
    }

    public async Task InstallPathsAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var plan = _installer.Analyze(path, SelectedType?.Type.Key);

            if (plan.NeedsTypeChoice)
            {
                var choice = await Interaction.PickInstallTypeAsync(plan);
                if (choice is null)
                {
                    Status = $"Skipped {Path.GetFileName(path)}.";
                    continue;
                }
                _installer.Finalize(plan, choice.Type, choice.Subfolder);
            }

            if (!plan.Ready)
            {
                var message = string.Join(Environment.NewLine, plan.Warnings);
                await Interaction.ShowMessageAsync(
                    $"Cannot install {Path.GetFileName(path)}",
                    message.Length > 0 ? message : "No installable files were found.");
                continue;
            }

            Status = $"Installing {plan.SourceName}…";
            var progress = new Progress<InstallProgress>(p =>
                Status = $"Installing {plan.SourceName}: {p.Done}/{p.Total} files…");

            var result = await _installer.InstallAsync(plan, progress);
            Status = result.Message;
            if (!result.Success && result.Errors.Count > 0)
            {
                await Interaction.ShowMessageAsync("Install problems", string.Join(Environment.NewLine, result.Errors.Take(12)));
            }
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task EnableSelected()
    {
        var items = Types.SelectMany(t => t.SelectedItems).ToList();
        if (items.Count == 0)
        {
            Status = "Select mods first (checkbox on a tile, or “Select all”).";
            return;
        }

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (item.IsEnabled) continue;
                var result = _toggle.SetEnabled(item.Entry, true);
                if (!result.Success) _log.Warn(result.Message);
            }
        });

        foreach (var item in items) { item.RefreshDetails(); }
        foreach (var typeVm in Types) typeVm.RecomputeCounts();
        UpdateSummary();
        Status = $"Enabled {items.Count} mods.";
    }

    [RelayCommand]
    private async Task DisableSelected()
    {
        var items = Types.SelectMany(t => t.SelectedItems).ToList();
        if (items.Count == 0)
        {
            Status = "Select mods first (checkbox on a tile, or “Select all”).";
            return;
        }

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (!item.IsEnabled) continue;
                var result = _toggle.SetEnabled(item.Entry, false);
                if (!result.Success) _log.Warn(result.Message);
            }
        });

        foreach (var item in items) { item.RefreshDetails(); }
        foreach (var typeVm in Types) typeVm.RecomputeCounts();
        UpdateSummary();
        Status = $"Disabled {items.Count} mods.";
    }

    [RelayCommand]
    private async Task RemoveSelected()
    {
        var items = Types.SelectMany(t => t.SelectedItems).ToList();
        if (items.Count == 0)
        {
            Status = "Select mods first (checkbox on a tile, or “Select all”).";
            return;
        }

        var confirmed = await Interaction.ConfirmAsync(
            "Move to trash?",
            $"Move {items.Count} mods to data/trash? You can restore them from there.",
            "Move to trash");
        if (!confirmed) return;

        foreach (var item in items)
        {
            HandleRemove(item);
        }

        foreach (var typeVm in Types)
        {
            foreach (var item in items.Where(i => typeVm.AllItems.Contains(i)).ToList())
            {
                typeVm.AllItems.Remove(item);
            }
            typeVm.ApplyFilter();
            typeVm.RecomputeCounts();
        }
        UpdateSummary();
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        var saved = await Interaction.ShowSettingsAsync(_config);
        if (!saved) return;

        ApplySettingsChange();
        await RefreshAsync();
    }

    /// <summary>Re-reads settings the user may have changed in the settings dialog.</summary>
    public void ApplySettingsChange()
    {
        ModsRoot = _config.Config.Game.ModsRoot;
        HasModsFolder = Directory.Exists(ModsRoot);
        OnPropertyChanged(nameof(GameName));
        foreach (var type in Types)
        {
            type.HideDisabled = _config.Config.Ui.HideDisabledByDefault;
        }
        StaticsReset();
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (HasModsFolder) OpenPath(ModsRoot);
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenPath(_config.Data.Root);

    [RelayCommand]
    private void LaunchGame()
    {
        var uri = _config.Config.Game.LaunchUri;
        try
        {
            OpenPath(uri);
            Status = $"Launching {GameName}…";
        }
        catch (Exception ex)
        {
            _log.Error("Could not launch game", ex);
            Status = $"Could not launch game: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenLogs() => OpenPath(_config.Data.Logs);

    public void OpenPath(string path)
    {
        try
        {
            var process = new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false
            };
            process.ArgumentList.Add(path);
            Process.Start(process);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open {path}", ex);
            Status = $"Could not open: {ex.Message}";
        }
    }

    private void StaticsReset()
    {
        foreach (var typeVm in Types)
        {
            typeVm.Load(Array.Empty<ModEntry>());
        }
        Summary = string.Empty;
    }

    public void Initialize()
    {
        ModsRoot = _config.Config.Game.ModsRoot;
        HasModsFolder = Directory.Exists(ModsRoot);
    }
}
