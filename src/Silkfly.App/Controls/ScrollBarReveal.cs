using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Silkfly.App.Controls;

// Thin scroll bars that widen next to the pointer (styles in Controls.axaml).
public static class ScrollBarReveal
{
    private const double Distance = 24;
    private const string NearClass = "near";

    public static void Register()
    {
        // handledEventsToo: the scroll bar handles the moves over itself
        InputElement.PointerMovedEvent.AddClassHandler<ScrollViewer>(
            (viewer, e) => viewer.Classes.Set(NearClass, IsNear(viewer, e)),
            RoutingStrategies.Bubble, handledEventsToo: true);
        // leaving through the window's resize border keeps the bar open
        InputElement.PointerExitedEvent.AddClassHandler<ScrollViewer>(
            (viewer, e) => viewer.Classes.Set(NearClass, IsNear(viewer, e)),
            RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static bool IsNear(ScrollViewer viewer, PointerEventArgs e)
    {
        var p = e.GetPosition(viewer);
        return p.X >= viewer.Bounds.Width - Distance && p.Y >= 0 && p.Y <= viewer.Bounds.Height;
    }
}
