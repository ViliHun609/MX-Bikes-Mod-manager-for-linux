using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

public sealed partial class ModItemViewModel : ObservableObject
{
    private bool _syncing;

    public ModItemViewModel(
        ModEntry entry,
        Action<string>? openFolder = null,
        Action<string>? copyPath = null)
    {
        Entry = entry;
        _isEnabled = entry.IsEnabled;

        OpenFolderCommand = new RelayCommand(() =>
        {
            if (openFolder is not null) openFolder(Entry.Path);
        });
        CopyPathCommand = new RelayCommand(() =>
        {
            if (copyPath is not null) copyPath(Entry.Path);
        });
    }

    public ModEntry Entry { get; }

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }

    public event EventHandler<bool>? ToggleRequested;

    public string Name => Entry.DisplayName;
    public string BaseName => Entry.BaseName;
    public string? Subtitle => Entry.Subtitle;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public string Category => Entry.Category;
    public bool HasCategory => Category.Length > 0;
    public string KindLabel => Entry.Kind == ModKind.PkzArchive ? "PKZ" : "Folder";
    public string SizeText => PathUtil.FormatSize(Entry.SizeBytes);
    public string PathText => Entry.Path;
    public string Monogram
    {
        get
        {
            var name = Entry.DisplayName.Trim();
            if (name.Length == 0) return "?";

            // Prefer the first word ("AK Ducati…" → "AK", "85cc_ALPHA…" → "85C") over a lone digit.
            var token = name.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
            return token.Length <= 3 ? token.ToUpperInvariant() : token[..3].ToUpperInvariant();
        }
    }

    public double TileOpacity => IsEnabled ? 1.0 : 0.45;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileOpacity))]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _previewImage;

    public bool HasPreview => PreviewImage is not null;

    partial void OnPreviewImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing) return;
        ToggleRequested?.Invoke(this, value);
    }

    /// <summary>Restores the checkbox state without triggering another rename.</summary>
    public void RevertEnabled(bool enabled)
    {
        _syncing = true;
        IsEnabled = enabled;
        _syncing = false;
    }

    public void RefreshDetails()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BaseName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(PathText));
        OnPropertyChanged(nameof(Monogram));
        OnPropertyChanged(nameof(TileOpacity));
    }
}
