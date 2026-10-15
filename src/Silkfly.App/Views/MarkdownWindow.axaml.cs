using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Views;

public sealed partial class MarkdownWindow : Window
{
    public MarkdownWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else
        {
            CloseButton.IsVisible = false;
        }
    }

    public MarkdownWindow(IShellService shell)
        : this()
    {
        if (Viewer.Engine is Markdown.Avalonia.IMarkdownEngine engine)
        {
            engine.HyperlinkCommand = new RelayCommand<string>(url =>
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    shell.OpenUrl(uri);
            });
        }
    }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
