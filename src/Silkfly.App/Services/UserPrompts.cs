using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Silkfly.App.ViewModels;
using Silkfly.App.Views;
using Silkfly.Core.Dependencies;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;
using Silkfly.Core.Operations;
using Silkfly.Core.Profiles;

namespace Silkfly.App.Services;

public sealed class UserPrompts(ILocalizer localizer) : IUserPrompts
{
    public Task<bool> ConfirmDependenciesAsync(PackageId target, IReadOnlyList<PlannedInstall> extra, IReadOnlyList<ResolutionIssue> issues)
    {
        var items = extra.Select(e => new DialogItem($"{e.Version.PackageId.DisplayName} {e.Version.Version}")).ToList();
        var problems = issues
            .Where(i => i.Kind is ResolutionIssueKind.NotInCatalog or ResolutionIssueKind.ConflictingMajor or ResolutionIssueKind.VersionNotFound)
            .Select(i => localizer.Format($"Prompt_Issue_{i.Kind}", i.Id.DisplayName, i.Detail))
            .ToList();

        return ShowAsync(new MessageDialogViewModel(
            localizer.Format("Prompt_Deps_Title", target.DisplayName),
            items.Count > 0 ? localizer["Prompt_Deps_Text"] : null,
            localizer["Mod_Install"],
            localizer["Dialog_Cancel"])
        {
            Items = items,
            Note = problems.Count > 0 ? string.Join("\n", problems) : null,
        });
    }

    public Task<bool> ConfirmBreakingDependentsAsync(PackageId target, IReadOnlyList<InstalledMod> dependents, bool disabling) =>
        ShowAsync(new MessageDialogViewModel(
            localizer.Format(disabling ? "Prompt_Dependents_Disable_Title" : "Prompt_Dependents_Remove_Title", target.DisplayName),
            localizer["Prompt_Dependents_Text"],
            disabling ? localizer["Prompt_Disable_Anyway"] : localizer["Prompt_Remove_Anyway"],
            localizer["Dialog_Cancel"],
            isDanger: true)
        {
            Items = dependents.Select(d => new DialogItem(d.Id.DisplayName)).ToList(),
        });

    public async Task<IReadOnlyList<PackageId>> PickOrphansToRemoveAsync(IReadOnlyList<InstalledMod> orphans)
    {
        var items = orphans.Select(o => (Mod: o, Item: new DialogItem($"{o.Id.DisplayName} {o.Version}", checkable: true))).ToList();
        var confirmed = await ShowAsync(new MessageDialogViewModel(
            localizer["Prompt_Orphans_Title"],
            localizer["Prompt_Orphans_Text"],
            localizer["Prompt_Orphans_Remove"],
            localizer["Prompt_Orphans_Keep"])
        {
            Items = items.Select(i => i.Item).ToList(),
        });

        return confirmed ? items.Where(i => i.Item.IsChecked).Select(i => i.Mod.Id).ToList() : [];
    }

    public Task<bool> ConfirmEnableDependenciesAsync(PackageId target, IReadOnlyList<InstalledMod> disabledDependencies) =>
        ShowAsync(new MessageDialogViewModel(
            localizer["Prompt_EnableDeps_Title"],
            localizer.Format("Prompt_EnableDeps_Text", target.DisplayName),
            localizer["Prompt_EnableDeps_Yes"],
            localizer["Prompt_EnableDeps_No"])
        {
            Items = disabledDependencies.Select(d => new DialogItem(d.Id.DisplayName)).ToList(),
        });

    private static Task<bool> ShowAsync(MessageDialogViewModel vm) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null)
                return false;
            return await new MessageWindow { DataContext = vm }.ShowDialog<bool>(owner);
        });
}
