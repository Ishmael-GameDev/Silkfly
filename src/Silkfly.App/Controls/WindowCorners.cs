using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Silkfly.App.Controls;

// On Windows our windows draw their own frame: a transparent window with a rounded outline border inside.
public static class WindowCorners
{
    private const double Radius = 6;

    public static void Apply(Window window, Border frame)
    {
        if (!OperatingSystem.IsWindows())
            return;

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = -1;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.Background = Brushes.Transparent;

        Update(window, frame);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                Update(window, frame);
        };
        frame.LayoutUpdated += (_, _) => ClipContent(window, frame);
    }

    private static void Update(Window window, Border frame)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        frame.CornerRadius = new CornerRadius(maximized ? 0 : Radius);
        frame.BorderThickness = new Thickness(maximized ? 0 : 1);
    }

    // clipping the frame itself would cut its outline at the corners: clip only what is inside it
    private static void ClipContent(Window window, Border frame)
    {
        if (frame.Child is not { } content)
            return;

        var radius = window.WindowState == WindowState.Maximized ? 0 : Radius - 1;
        var size = content.Bounds.Size;
        if (content.Clip is RectangleGeometry { RadiusX: var current } clip && current == radius && clip.Rect.Size == size)
            return;

        content.Clip = radius > 0 ? new RectangleGeometry(new Rect(size), radius, radius) : null;
    }
}
