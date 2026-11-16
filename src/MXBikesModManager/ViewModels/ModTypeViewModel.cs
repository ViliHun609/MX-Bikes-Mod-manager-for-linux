using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

public sealed partial class CategoryFilterViewModel : ObservableObject
{
    public CategoryFilterViewModel(string name, bool isAll = false)
    {
        Name = name;
        IsAll = isAll;
    }

    public string Name { get; }
    public bool IsAll { get; }

    [ObservableProperty]
    private int _count;

    public string DisplayName => IsAll ? "All" : Name;
    public string CountText => Count.ToString();
}

public sealed partial class ModTypeViewModel : ObservableObject
{
    private readonly Func<ModItemViewModel, bool, bool> _toggleHandler;
    private readonly Action<string> _status;

    public ModTypeViewModel(
        ModTypeConfig type,
        Func<ModItemViewModel, bool, bool> toggleHandler,
        Func<ModItemViewModel, bool> removeHandler,
        Action<string> openInFileManager,
        Action<string> copyPath,
        Action<string> status)
    {
        Type = type;
        _toggleHandler = toggleHandler;
        _status = status;
        OpenInFileManager = new RelayCommand<string?>(path =>
        {
            if (!string.IsNullOrEmpty(path)) openInFileManager(path);
        });
        RemoveCommand = new RelayCommand<ModItemViewModel?>(item =>
        {
            if (item is not null && removeHandler(item))
            {
                AllItems.Remove(item);
                FilteredItems.Remove(item);
                ApplyFilter();
            }
        });

        Categories.Add(new CategoryFilterViewModel("All", isAll: true));
        _selectedCategory = Categories[0];
        _openInFileManager = openInFileManager;
        _copyPath = copyPath;
    }

    private readonly Action<string> _openInFileManager;
    private readonly Action<string> _copyPath;

    public MainWindowViewModel? Owner { get; set; }

    public ModTypeConfig Type { get; }
    public ObservableCollection<ModItemViewModel> AllItems { get; } = new();
    public ObservableCollection<ModItemViewModel> FilteredItems { get; } = new();
    public ObservableCollection<CategoryFilterViewModel> Categories { get; } = new();

    public string Title => Type.DisplayTitle;
    public Geometry IconGeometry => AppIcons.Resolve(Type.Icon);
    public bool HasCategories => Categories.Count > 1;

    public RelayCommand<string?> OpenInFileManager { get; }
    public RelayCommand<ModItemViewModel?> RemoveCommand { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hideDisabled;

    [ObservableProperty]
    private bool _smoothScrolling = true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private CategoryFilterViewModel? _selectedCategory;

    [ObservableProperty]
    private string _sortMode = "Name";

    [ObservableProperty]
    private int _enabledCount;

    public int TotalCount => AllItems.Count;

    public string CounterText => $"{EnabledCount}/{TotalCount} enabled";

    public IReadOnlyList<string> SortModes { get; } = new[] { "Name", "Name (Z-A)", "Category", "Largest", "Newest" };

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnHideDisabledChanged(bool value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(CategoryFilterViewModel? value) => ApplyFilter();
    partial void OnSortModeChanged(string value) => ApplyFilter();

    public void Load(IEnumerable<ModEntry> entries)
    {
        AllItems.Clear();
        Categories.Clear();
        Categories.Add(new CategoryFilterViewModel("All", isAll: true));

        foreach (var entry in entries)
        {
            var item = new ModItemViewModel(entry, _openInFileManager, _copyPath);
            item.ToggleRequested += (_, enabled) => _toggleHandler(item, enabled);
            AllItems.Add(item);
        }

        // Categories can come from subfolders or from name rules (e.g. MX1OEM_, 85cc_).
        foreach (var group in AllItems
                     .Where(i => !string.IsNullOrEmpty(i.Category))
                     .GroupBy(i => i.Category)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            Categories.Add(new CategoryFilterViewModel(group.Key) { Count = group.Count() });
        }

        Categories[0].Count = AllItems.Count;
        SelectedCategory = Categories[0];
        OnPropertyChanged(nameof(HasCategories));
        RecomputeCounts();
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        IEnumerable<ModItemViewModel> query = AllItems;

        if (HideDisabled) query = query.Where(i => i.IsEnabled);

        if (SelectedCategory is { IsAll: false } category)
        {
            query = query.Where(i => string.Equals(i.Category, category.Name, StringComparison.OrdinalIgnoreCase));
        }

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(i =>
                i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                i.BaseName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (i.Subtitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                i.Category.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        query = SortMode switch
        {
            "Name (Z-A)" => query.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "Category" => query
                .OrderBy(i => string.IsNullOrEmpty(i.Category) ? "\uffff" : i.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "Largest" => query.OrderByDescending(i => i.Entry.SizeBytes),
            "Newest" => query.OrderByDescending(i => LastWrite(i)),
            _ => query.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };

        FilteredItems.Clear();
        foreach (var item in query) FilteredItems.Add(item);

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public int VisibleCount => FilteredItems.Count;
    public bool IsEmpty => FilteredItems.Count == 0;
    public string EmptyMessage => AllItems.Count == 0
        ? $"No {Title.ToLowerInvariant()} found in this folder."
        : "Nothing matches the current search/filter.";

    public void RecomputeCounts()
    {
        EnabledCount = AllItems.Count(i => i.IsEnabled);
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CounterText));
        if (Categories.Count > 0)
        {
            Categories[0].Count = AllItems.Count;
        }
    }

    public IEnumerable<ModItemViewModel> SelectedItems => AllItems.Where(i => i.IsSelected);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in FilteredItems) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in AllItems) item.IsSelected = false;
    }

    private static DateTime LastWrite(ModItemViewModel item)
    {
        try
        {
            return item.Entry.Kind == ModKind.PkzArchive
                ? new FileInfo(item.Entry.Path).LastWriteTimeUtc
                : Directory.GetLastWriteTimeUtc(item.Entry.Path);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }
}
