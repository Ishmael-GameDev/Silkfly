using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Silkfly.App.ViewModels;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Operations;
using Silkfly.Core.Profiles;

namespace Silkfly.App.Services;

public sealed class ProfileSharing(
    IProfileTransfer transfer,
    IProfileService profiles,
    IOperationQueue queue,
    IFilePicker picker,
    IDialogService dialogs,
    IToastService toasts,
    ILocalizer localizer,
    ILogger<ProfileSharing> logger)
{
    private FilePickerFileType R2zType => new(localizer["Profiles_FileType"]) { Patterns = ["*.r2z"] };

    public async Task ExportToFileAsync(Profile profile)
    {
        var file = await picker.PickSaveFileAsync(localizer["Profiles_ExportPickTitle"], $"{profile.Name}.r2z", R2zType);
        if (file is null)
            return;

        await RunAsync("Profiles_ExportFile", async () =>
        {
            var result = await transfer.ExportR2zAsync(profile, file, CancellationToken.None);
            toasts.Show(localizer.Format("Profiles_Exported", result.ModCount, result.ConfigCount), ToastKind.Success);
            if (result.SkippedManual.Count > 0)
            {
                toasts.Show(localizer.Format("Profiles_ExportedSkipped", string.Join(", ", result.SkippedManual.Select(i => i.DisplayName))),
                    ToastKind.Warning);
            }
        });
    }

    public async Task ShareCodeAsync(Profile profile)
    {
        var confirmed = await dialogs.ConfirmAsync(localizer["Profiles_Code_Confirm_Title"],
            localizer.Format("Profiles_Code_Confirm_Text", profile.Name), localizer["Profiles_Code_Upload"]);
        if (!confirmed)
            return;

        await RunAsync("Profiles_ExportCode", async () =>
        {
            var code = await transfer.ExportCodeAsync(profile, CancellationToken.None);
            await dialogs.ShowTextAsync(localizer["Profiles_Code_Title"], localizer["Profiles_Code_Text"], code);
        });
    }

    public async Task ImportFromFileAsync()
    {
        var file = await picker.PickFileAsync(localizer["Profiles_ImportPickTitle"], [R2zType]);
        if (file is not null)
            await RunAsync("Profiles_ImportFile", async () => await ImportAsync(await transfer.ReadR2zAsync(file, CancellationToken.None)));
    }

    public async Task ImportFromCodeAsync()
    {
        var code = await dialogs.PromptAsync(localizer["Profiles_ImportCode_Title"], localizer["Profiles_ImportCode_Label"], string.Empty,
            c => Guid.TryParse(c.Trim(), out _) ? null : localizer["Error_BadProfileCode"]);
        if (code is not null)
            await RunAsync("Profiles_ImportCode", async () => await ImportAsync(await transfer.ReadCodeAsync(code, CancellationToken.None)));
    }

    private async Task ImportAsync(ProfileImport import)
    {
        var name = await dialogs.PromptAsync(localizer["Profiles_Import_Title"], localizer["Profiles_Name"], UniqueName(import.ProfileName),
            n => Validate(n), ProfileService.MaxNameLength);
        if (name is null)
            return;

        var profile = await transfer.CreateFromImportAsync(import, name, CancellationToken.None);
        await profiles.SetActiveAsync(profile, CancellationToken.None);
        queue.EnqueueInstallSet(profile, import.Mods);
        toasts.Show(localizer.Format("Profiles_Importing", profile.Name, import.Mods.Count));
    }

    private string? Validate(string name)
    {
        try
        {
            profiles.ValidateName(name.Trim());
            return null;
        }
        catch (SilkflyException ex)
        {
            return localizer.Format(ex.MessageKey, [.. ex.Args]);
        }
    }

    private string UniqueName(string name)
    {
        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var candidate = ProfilesViewModel.FitName(clean, n => n);
        for (var i = 2; Validate(candidate) is not null && i < 100; i++)
            candidate = ProfilesViewModel.FitName(clean, n => $"{n} ({i})");
        return candidate;
    }

    private async Task RunAsync(string actionKey, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "{Action} failed", actionKey);
            toasts.ShowError(actionKey, ex);
        }
    }
}
