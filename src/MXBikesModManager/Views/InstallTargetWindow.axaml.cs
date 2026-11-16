using Avalonia.Controls;
using Avalonia.Interactivity;
using MXBikesModManager.Models;
using MXBikesModManager.Services;
using MXBikesModManager.ViewModels;

namespace MXBikesModManager.Views;

public partial class InstallTargetWindow : Window
{
    private const string PaintTypeKey = "bike-paint";

    private readonly List<ModTypeConfig> _types = new();
    private readonly IReadOnlyList<string> _paintBikes = Array.Empty<string>();
    private readonly InstallPlan? _plan;
    private ModTypeConfig? _bikeType;

    public InstallTargetWindow()
    {
        InitializeComponent();
    }

    public InstallTargetWindow(
        InstallPlan plan,
        IEnumerable<ModTypeConfig> types,
        IReadOnlyList<string>? paintBikes = null) : this()
    {
        _plan = plan;
        _types.AddRange(types);
        _bikeType = _types.FirstOrDefault(t => string.Equals(t.Key, "bikes", StringComparison.OrdinalIgnoreCase));
        _paintBikes = paintBikes ?? Array.Empty<string>();

        FileText.Text = Path.GetFileName(plan.SourcePath);
        KindText.Text = plan.IsBikePaint
            ? $"Detected: {plan.DetectedKind} · choose the bike it belongs to"
            : $"Detected: {plan.DetectedKind} · {DescribeCount(plan)}";

        PaintBikeBox.ItemsSource = _paintBikes;

        var options = BuildOptions();
        TypeList.ItemsSource = options;

        if (plan.IsBikePaint)
        {
            TypeList.SelectedItem = options.FirstOrDefault(o => o.Key == PaintTypeKey);
        }
        else
        {
            var preferred = options.FirstOrDefault(o =>
                    o.Key == plan.Type?.Key ||
                    o.Key == plan.DetectedTypeKey)
                ?? options.FirstOrDefault();
            TypeList.SelectedItem = preferred;
        }

        ApplySelection(TypeList.SelectedItem as ModTypeConfig);

        PaintBikeBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutoCompleteBox.TextProperty &&
                TypeList.SelectedItem is ModTypeConfig selected)
            {
                UpdateTarget(selected);
            }
        };

        SubfolderBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutoCompleteBox.TextProperty &&
                TypeList.SelectedItem is ModTypeConfig selected)
            {
                UpdateTarget(selected);
            }
        };

        if (plan.Warnings.Count > 0)
        {
            WarningText.Text = string.Join(Environment.NewLine, plan.Warnings);
            WarningText.IsVisible = true;
        }
        else if (plan.IsBikePaint && _paintBikes.Count == 0)
        {
            ShowWarning("No bike folders were found in mods/bikes. Install the bike first, then add paints.");
        }
    }

    private static string DescribeCount(InstallPlan plan)
    {
        var count = plan.Files.Count > 0
            ? plan.Files.Count
            : plan.IsDirectCopy ? 1 : plan.Entries.Count;
        return count == 1 ? "1 file" : $"{count} files";
    }

    private List<ModTypeConfig> BuildOptions()
    {
        var options = new List<ModTypeConfig>();

        // Put the destinations the app can reason about first, paint right after bikes.
        var order = new[] { "bikes", PaintTypeKey, "rider", "tyres", "tracks", "misc" };
        foreach (var key in order)
        {
            if (key == PaintTypeKey)
            {
                if (_bikeType is not null)
                {
                    options.Add(new ModTypeConfig
                    {
                        Key = PaintTypeKey,
                        Folder = "bikes/<bike>/paints",
                        Title = "Bike paint",
                        Icon = "🖌"
                    });
                }
                continue;
            }

            var match = _types.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match is not null) options.Add(match);
        }

        options.AddRange(_types.Where(t => order.All(k =>
            !string.Equals(k, t.Key, StringComparison.OrdinalIgnoreCase))));

        return options;
    }

    private void ApplySelection(ModTypeConfig? type)
    {
        if (type is null) return;

        var isPaint = IsPaint(type);
        PaintPanel.IsVisible = isPaint;

        if (isPaint && string.IsNullOrWhiteSpace(PaintBikeBox.Text) && _plan is not null)
        {
            var suggested = _plan.PaintBikeFolder
                            ?? InstallService.SuggestPaintBike(_plan.SourcePath, _paintBikes);
            if (!string.IsNullOrWhiteSpace(suggested)) PaintBikeBox.Text = suggested;
        }

        var subfolders = isPaint ? new List<string>() : GetSubfolders(type);
        SubfolderPanel.IsVisible = subfolders.Count > 0;

        if (!isPaint && subfolders.Count > 0)
        {
            SubfolderBox.ItemsSource = subfolders;
            var suggested = _plan is null ? null : InstallService.SuggestSubfolder(_plan.SourcePath, type);
            SubfolderBox.Text = suggested ?? type.InstallSubfolder;
        }
        else if (!isPaint)
        {
            SubfolderBox.Text = string.Empty;
        }

        UpdateTarget(type);
        HideWarning();
    }

    private static List<string> GetSubfolders(ModTypeConfig type)
    {
        var subfolders = new List<string>();

        if (!string.IsNullOrWhiteSpace(type.InstallSubfolder))
        {
            subfolders.Add(type.InstallSubfolder);
        }

        foreach (var subfolder in type.ScanSubfolders)
        {
            if (!string.IsNullOrWhiteSpace(subfolder) &&
                !subfolders.Contains(subfolder, StringComparer.OrdinalIgnoreCase))
            {
                subfolders.Add(subfolder);
            }
        }

        return subfolders
            .OrderBy(subfolder => subfolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateTarget(ModTypeConfig type)
    {
        if (IsPaint(type))
        {
            var bike = PaintBikeBox.Text?.Trim();
            TargetText.Text = string.IsNullOrWhiteSpace(bike)
                ? "Target: bikes/<bike>/paints/"
                : $"Target: bikes/{bike}/paints/";
            return;
        }

        var subfolder = SubfolderBox.Text?.Trim();
        var root = string.IsNullOrWhiteSpace(subfolder)
            ? type.Folder
            : $"{type.Folder}/{subfolder}";

        if (_plan is null)
        {
            TargetText.Text = $"Target: {root}/";
        }
        else if (_plan.IsDirectCopy)
        {
            TargetText.Text = $"Target: {root}/{Path.GetFileName(_plan.SourcePath)}";
        }
        else if (_plan.IsFolderCopy)
        {
            var name = Path.GetFileName(_plan.SourcePath.TrimEnd(Path.DirectorySeparatorChar));
            TargetText.Text = $"Target: {root}/{name}";
        }
        else
        {
            TargetText.Text = $"Target: {root}/  (archive contents)";
        }
    }

    private static bool IsPaint(ModTypeConfig type) =>
        string.Equals(type.Key, PaintTypeKey, StringComparison.OrdinalIgnoreCase);

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplySelection(TypeList.SelectedItem as ModTypeConfig);
    }

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (TypeList.SelectedItem is not ModTypeConfig type) return;

        if (IsPaint(type))
        {
            if (_bikeType is null)
            {
                ShowWarning("The bikes mod type is not configured.");
                return;
            }

            var typed = PaintBikeBox.Text?.Trim() ?? string.Empty;
            if (typed.Length == 0)
            {
                ShowWarning("Choose the bike this paint belongs to.");
                return;
            }

            var bike = _paintBikes.FirstOrDefault(b =>
                           string.Equals(b, typed, StringComparison.OrdinalIgnoreCase))
                       ?? _paintBikes.FirstOrDefault(b =>
                           b.Contains(typed, StringComparison.OrdinalIgnoreCase));

            if (bike is null)
            {
                ShowWarning($"“{typed}” is not a bike folder. Pick one from the list.");
                return;
            }

            Close(new InstallChoice(_bikeType, null, bike));
            return;
        }

        var subfolder = string.IsNullOrWhiteSpace(SubfolderBox.Text) ? null : SubfolderBox.Text.Trim();
        if (subfolder is not null &&
            (subfolder.Contains('/') || subfolder.Contains('\\') ||
             subfolder is "." or ".."))
        {
            ShowWarning("The subfolder must be a single folder name (for example “boots”).");
            return;
        }

        Close(new InstallChoice(type, subfolder));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowWarning(string message)
    {
        WarningText.Text = message;
        WarningText.IsVisible = true;
    }

    private void HideWarning()
    {
        WarningText.Text = string.Empty;
        WarningText.IsVisible = false;
    }
}
