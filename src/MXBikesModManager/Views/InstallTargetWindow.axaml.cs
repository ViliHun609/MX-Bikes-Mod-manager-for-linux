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
        _types.AddRange(types);
        _bikeType = _types.FirstOrDefault(t => string.Equals(t.Key, "bikes", StringComparison.OrdinalIgnoreCase));
        _paintBikes = paintBikes ?? Array.Empty<string>();

        FileText.Text = Path.GetFileName(plan.SourcePath);
        KindText.Text = plan.IsBikePaint
            ? $"Detected: {plan.DetectedKind} · choose the bike it belongs to"
            : $"Detected: {plan.DetectedKind} · {plan.Files.Count} files planned";

        PaintBikeBox.ItemsSource = _paintBikes;

        var options = BuildOptions();
        TypeList.ItemsSource = options;

        if (plan.IsBikePaint)
        {
            TypeList.SelectedItem = options.FirstOrDefault(o => o.Key == PaintTypeKey);
            var suggested = plan.PaintBikeFolder
                            ?? InstallService.SuggestPaintBike(plan.SourcePath, _paintBikes);
            if (!string.IsNullOrWhiteSpace(suggested)) PaintBikeBox.Text = suggested;
        }
        else
        {
            var preferred = options.FirstOrDefault(o =>
                    o.Key == plan.DetectedTypeKey ||
                    (plan.Type is not null && o.Key == plan.Type.Key))
                ?? options.FirstOrDefault();
            TypeList.SelectedItem = preferred;
        }

        ApplySelection(TypeList.SelectedItem as ModTypeConfig);

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
        SubfolderPanel.IsVisible = !isPaint;
        if (!isPaint) SubfolderBox.Text = type.InstallSubfolder;
        HideWarning();
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
