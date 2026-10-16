using Avalonia.Controls;

namespace Silkfly.App.Controls;

// Puts a page back where it was scrolled to last time.
public static class ScrollMemory
{
    public static void Keep(ScrollViewer viewer, Func<double> load, Action<double> save)
    {
        var restored = false;
        viewer.Loaded += (_, _) =>
        {
            viewer.Offset = viewer.Offset.WithY(load());
            restored = true;
        };
        viewer.ScrollChanged += (_, _) =>
        {
            if (restored && viewer.IsLoaded)
                save(viewer.Offset.Y);
        };
    }
}
