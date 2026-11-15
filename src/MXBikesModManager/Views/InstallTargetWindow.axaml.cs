using Avalonia.Controls;
using Avalonia.Interactivity;
using MXBikesModManager.Models;
using MXBikesModManager.Services;
using MXBikesModManager.ViewModels;

namespace MXBikesModManager.Views;

public partial class InstallTargetWindow : Window
{
    public InstallTargetWindow()
    {
        InitializeComponent();
    }

    public InstallTargetWindow(InstallPlan plan, IEnumerable<ModTypeConfig> types) : this()
    {
        FileText.Text = Path.GetFileName(plan.SourcePath);
        KindText.Text = $"Detected: {plan.DetectedKind} · {plan.Files.Count} files planned";

        var list = types.ToList();
        TypeList.ItemsSource = list;
        TypeList.SelectedIndex = list.Count > 0 ? 0 : -1;

        if (plan.Warnings.Count > 0)
        {
            WarningText.Text = string.Join(Environment.NewLine, plan.Warnings);
            WarningText.IsVisible = true;
        }
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TypeList.SelectedItem is ModTypeConfig type)
        {
            SubfolderBox.Text = type.InstallSubfolder;
        }
    }

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (TypeList.SelectedItem is not ModTypeConfig type) return;
        var subfolder = string.IsNullOrWhiteSpace(SubfolderBox.Text) ? null : SubfolderBox.Text.Trim();
        Close(new InstallChoice(type, subfolder));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
