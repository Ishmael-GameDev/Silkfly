using Avalonia.Controls;
using Silkfly.App.Controls;
using Silkfly.App.ViewModels;

namespace Silkfly.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ScrollMemory.Keep(Scroll, () => (DataContext as SettingsViewModel)?.ScrollOffset ?? 0, y =>
        {
            if (DataContext is SettingsViewModel vm)
                vm.ScrollOffset = y;
        });
    }
}
