using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed partial class ToastViewModel(string text, ToastKind kind, string? details, Action<ToastViewModel> close, Action<string>? showDetails)
    : ObservableObject
{
    public string Text { get; } = text;

    public ToastKind Kind { get; } = kind;

    public string? Details { get; } = details;

    public bool HasDetails => Details is not null && showDetails is not null;

    public bool IsError => Kind == ToastKind.Error;

    public bool IsWarning => Kind == ToastKind.Warning;

    public bool IsSuccess => Kind == ToastKind.Success;

    [RelayCommand]
    private void Close() => close(this);

    [RelayCommand]
    private void ShowDetails()
    {
        if (Details is not null)
            showDetails?.Invoke(Details);
        close(this);
    }
}

public interface IToastService
{
    ObservableCollection<ToastViewModel> Toasts { get; }

    void Show(string text, ToastKind kind = ToastKind.Info, string? details = null);

    void ShowError(string messageKey, Exception error, params object[] args);
}

public sealed class ToastService(ILocalizer localizer, Lazy<IDialogService> dialogs) : IToastService
{
    private const int MaxToasts = 3;

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void Show(string text, ToastKind kind = ToastKind.Info, string? details = null) =>
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastViewModel(text, kind, details, Remove,
                d => _ = dialogs.Value.ShowErrorAsync(text, d));
            Toasts.Add(toast);
            while (Toasts.Count > MaxToasts)
                Toasts.RemoveAt(0);

            var lifetime = kind is ToastKind.Error or ToastKind.Warning ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(4);
            DispatcherTimer.RunOnce(() => Remove(toast), lifetime);
        });

    public void ShowError(string messageKey, Exception error, params object[] args)
    {
        var reason = error is SilkflyException domain ? localizer.Format(domain.MessageKey, [.. domain.Args]) : error.Message;
        var text = args.Length > 0 ? localizer.Format(messageKey, args) : localizer[messageKey];
        Show($"{text}: {reason}", ToastKind.Error, error.ToString());
    }

    private void Remove(ToastViewModel toast) => Toasts.Remove(toast);
}
