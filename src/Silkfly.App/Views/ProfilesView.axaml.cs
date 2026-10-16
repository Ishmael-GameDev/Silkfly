using Avalonia.Controls;
using Silkfly.App.Controls;
using Silkfly.App.ViewModels;

namespace Silkfly.App.Views;

public sealed partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        ScrollMemory.Keep(Scroll, () => (DataContext as ProfilesViewModel)?.ScrollOffset ?? 0, y =>
        {
            if (DataContext is ProfilesViewModel vm)
                vm.ScrollOffset = y;
        });
    }
}
