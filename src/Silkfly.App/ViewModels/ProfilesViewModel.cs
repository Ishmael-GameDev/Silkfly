using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Silkfly.App.Services;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Profiles;

namespace Silkfly.App.ViewModels;

public sealed partial class ProfilesViewModel(
    SessionViewModel session,
    IProfileService profiles,
    IDialogService dialogs,
    IShellService shell,
    IToastService toasts,
    ProfileSharing sharing,
    ILocalizer localizer,
    ILogger<ProfilesViewModel> logger) : ObservableObject
{
    public SessionViewModel Session => session;

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = await dialogs.PromptAsync(
            localizer["Profiles_Create_Title"], localizer["Profiles_Name"], string.Empty, n => session.ValidateProfileName(n),
            ProfileService.MaxNameLength);
        if (name is null)
            return;

        await RunAsync("Profiles_Create", async () =>
        {
            await profiles.CreateAsync(name, CancellationToken.None);
            toasts.Show(localizer.Format("Profiles_Created", name), ToastKind.Success);
        });
    }

    [RelayCommand]
    private async Task RenameAsync(ProfileItemViewModel item)
    {
        var name = await dialogs.PromptAsync(
            localizer["Profiles_Rename_Title"], localizer["Profiles_Name"], item.Name, n => session.ValidateProfileName(n, item.Profile),
            ProfileService.MaxNameLength);
        if (name is null || name == item.Name)
            return;

        await RunAsync("Profiles_Rename", () => profiles.RenameAsync(item.Profile, name, CancellationToken.None));
    }

    [RelayCommand]
    private async Task DuplicateAsync(ProfileItemViewModel item)
    {
        var suggested = FitName(item.Name, n => localizer.Format("Profiles_Duplicate_Name", n));
        var name = await dialogs.PromptAsync(
            localizer["Profiles_Duplicate"], localizer["Profiles_Name"], suggested, n => session.ValidateProfileName(n),
            ProfileService.MaxNameLength);
        if (name is null)
            return;

        await RunAsync("Profiles_Duplicate", () => profiles.DuplicateAsync(item.Profile, name, CancellationToken.None));
    }

    [RelayCommand]
    private async Task DeleteAsync(ProfileItemViewModel item)
    {
        var confirmed = await dialogs.ConfirmAsync(
            localizer["Profiles_Delete_Title"], localizer.Format("Profiles_Delete_Text", item.Name), localizer["Profiles_Delete"], danger: true);
        if (!confirmed)
            return;

        await RunAsync("Profiles_Delete", () => profiles.DeleteAsync(item.Profile, CancellationToken.None));
    }

    [RelayCommand]
    private Task SetActiveAsync(ProfileItemViewModel item) =>
        RunAsync("Profiles_SetActive", () => profiles.SetActiveAsync(item.Profile, CancellationToken.None));

    [RelayCommand]
    private void OpenFolder(ProfileItemViewModel item) => shell.OpenFolder(item.Profile.RootPath);

    [RelayCommand]
    private async Task PlayAsync(ProfileItemViewModel item)
    {
        if (!item.IsActive)
            await profiles.SetActiveAsync(item.Profile, CancellationToken.None);
        await session.LaunchAsync(item.Profile);
    }

    [RelayCommand]
    private Task ImportFileAsync() => sharing.ImportFromFileAsync();

    [RelayCommand]
    private Task ImportCodeAsync() => sharing.ImportFromCodeAsync();

    [RelayCommand]
    private void ToggleExpanded(ProfileItemViewModel item) => item.IsExpanded = !item.IsExpanded;

    private async Task RunAsync(string actionKey, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Profile action {Action} failed", actionKey);
            toasts.ShowError(actionKey, ex);
        }
    }

    internal static string FitName(string name, Func<string, string> format)
    {
        var result = format(name);
        var excess = result.Length - ProfileService.MaxNameLength;
        return excess <= 0 ? result : format(name[..Math.Max(1, name.Length - excess)].TrimEnd());
    }
}
