using MXBikesModManager.Models;
using MXBikesModManager.Services;

namespace MXBikesModManager.ViewModels;

public sealed record InstallChoice(ModTypeConfig Type, string? Subfolder);

/// <summary>Everything the view model needs from the window: pickers and dialogs.</summary>
public interface IUiInteraction
{
    Task<IReadOnlyList<string>> PickModArchivesAsync();
    Task<string?> PickModsFolderAsync();
    Task<InstallChoice?> PickInstallTypeAsync(InstallPlan plan);
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue");
    Task ShowMessageAsync(string title, string message);
    Task<bool> ShowSettingsAsync(ConfigService config);
}
