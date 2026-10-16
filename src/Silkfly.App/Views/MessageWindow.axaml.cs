using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Silkfly.App.Localization;
using Silkfly.App.ViewModels;

namespace Silkfly.App.Views;

public sealed partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            SystemDecorations = SystemDecorations.Full;
            Controls.WindowCorners.Apply(this, Frame);
        }

        CopyButton.Bind(ContentProperty, StrExtension.CreateBinding("Dialog_Copy"));
        OpenLogButton.Bind(ContentProperty, StrExtension.CreateBinding("Dialog_OpenLog"));
        Opened += (_, _) =>
        {
            if (DataContext is MessageDialogViewModel { HasInput: true })
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
        };
    }

    public Action? OpenLog { get; init; }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageDialogViewModel { Details: { } details } && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(details);
    }

    private void OnOpenLogClick(object? sender, RoutedEventArgs e) => OpenLog?.Invoke();
}
