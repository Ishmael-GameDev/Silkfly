using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Silkfly.App.ViewModels;
using Silkfly.App.Views;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Services;

public interface IDialogService
{
    Task ShowMarkdownAsync(string title, string subtitle, Func<CancellationToken, Task<string?>> load, string missingWhat);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false);

    Task<string?> PromptAsync(string title, string label, string initial, Func<string, string?>? validate = null, int maxLength = 0);

    Task ShowErrorAsync(string message, string details);

    Task ShowTextAsync(string title, string message, string text);
}

public sealed class DialogService(IShellService shell, ILocalizer localizer, IAppPaths paths) : IDialogService
{
    public async Task ShowMarkdownAsync(
        string title, string subtitle, Func<CancellationToken, Task<string?>> load, string missingWhat)
    {
        if (MainWindow is not { } owner)
            return;

        using var cts = new CancellationTokenSource();
        var vm = new MarkdownDialogViewModel(title, subtitle, load, missingWhat, localizer);
        var window = new MarkdownWindow(shell) { DataContext = vm };
        window.Closed += (_, _) => cts.Cancel();

        var loading = vm.LoadAsync(cts.Token);
        await window.ShowDialog(owner);
        await loading;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    {
        if (MainWindow is not { } owner)
            return false;

        var vm = new MessageDialogViewModel(title, message, confirmText, localizer["Dialog_Cancel"], danger);
        return await new MessageWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    public async Task<string?> PromptAsync(string title, string label, string initial, Func<string, string?>? validate = null, int maxLength = 0)
    {
        if (MainWindow is not { } owner)
            return null;

        var vm = new MessageDialogViewModel(title, null, localizer["Dialog_Ok"], localizer["Dialog_Cancel"],
            inputLabel: label, initialInput: initial, validate: validate) { InputMaxLength = maxLength };
        return await new MessageWindow { DataContext = vm }.ShowDialog<bool>(owner) ? vm.Input.Trim() : null;
    }

    public async Task ShowErrorAsync(string message, string details)
    {
        if (MainWindow is not { } owner)
            return;

        var vm = new MessageDialogViewModel(localizer["Dialog_Error_Title"], message, localizer["Dialog_Ok"], null, details: details);
        await new MessageWindow { DataContext = vm, OpenLog = () => shell.OpenFolder(paths.LogsRoot) }.ShowDialog<bool>(owner);
    }

    public async Task ShowTextAsync(string title, string message, string text)
    {
        if (MainWindow is not { } owner)
            return;

        var vm = new MessageDialogViewModel(title, message, localizer["Dialog_Ok"], null, details: text) { ShowOpenLog = false };
        await new MessageWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    private static Window? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
