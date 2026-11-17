using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

/// <summary>Install destination chosen in the dialog. <see cref="PaintBike"/> is set for bike paint jobs.</summary>
public sealed record InstallChoice(
    ModTypeConfig Type,
    string? Subfolder,
    string? PaintBike = null,
    bool TrashSource = false);

/// <summary>Everything the view model needs from the window: pickers and dialogs.</summary>
public interface IUiInteraction
{
    Task<IReadOnlyList<string>> PickModArchivesAsync();
    Task<InstallChoice?> PickInstallTypeAsync(InstallPlan plan, IReadOnlyList<string> paintBikes);
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue");
    Task ShowMessageAsync(string title, string message);
    Task<bool> ShowSettingsAsync(ConfigService config);
}
