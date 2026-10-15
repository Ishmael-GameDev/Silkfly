using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Silkfly.App.ViewModels;

namespace Silkfly.App.Views;

public sealed partial class ModsView : UserControl
{
    private ModListViewModel? _vm;

    public ModsView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropTarget(false));
        AddHandler(DragDrop.DropEvent, OnDrop);

        ModScroll.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, OnScrollTick);
    }

    private const double WheelStep = 100;
    private readonly DispatcherTimer _scrollTimer;
    private ScrollViewer? _scroll;
    private double _targetY;
    private double _lastSetY;

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        _scroll ??= ModScroll;
        if (_scroll is null || e.Delta.Y == 0)
            return;

        if (!_scrollTimer.IsEnabled || MovedElsewhere(_scroll))
        {
            _targetY = _scroll.Offset.Y;
            _lastSetY = _scroll.Offset.Y;
        }

        _targetY = Math.Clamp(_targetY - e.Delta.Y * WheelStep, 0, MaxScroll(_scroll));
        e.Handled = true;
        _scrollTimer.Start();
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (_scroll is null)
        {
            _scrollTimer.Stop();
            return;
        }

        // moved by the thumb, keyboard or a jump: don't pull it back
        if (MovedElsewhere(_scroll))
        {
            _scrollTimer.Stop();
            return;
        }

        _targetY = Math.Min(_targetY, MaxScroll(_scroll));
        var current = _scroll.Offset.Y;
        var next = current + (_targetY - current) * 0.55;
        if (Math.Abs(_targetY - next) < 0.3)
        {
            next = _targetY;
            _scrollTimer.Stop();
        }

        _scroll.Offset = _scroll.Offset.WithY(next);
        _lastSetY = _scroll.Offset.Y;
    }

    private bool MovedElsewhere(ScrollViewer scroll) => _scrollTimer.IsEnabled && Math.Abs(scroll.Offset.Y - _lastSetY) > 1;

    private static double MaxScroll(ScrollViewer scroll) => Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);

    private void OnAuthorBoxFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is AutoCompleteBox { IsDropDownOpen: false } box)
            box.IsDropDownOpen = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropTarget(hasFiles);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropTarget(false);
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths is { Count: > 0 })
            _vm?.InstallFiles(paths);
    }

    private void SetDropTarget(bool value)
    {
        if (_vm is not null)
            _vm.IsDropTarget = value;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.ScrollToRequested -= OnScrollToRequested;
        _vm = DataContext as ModListViewModel;
        if (_vm is not null)
            _vm.ScrollToRequested += OnScrollToRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
            _vm.ScrollToRequested -= OnScrollToRequested;
        _vm = null;
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnScrollToRequested(object? sender, ModItemViewModel card) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ModList.OffsetOf(card) is { } offset)
            {
                _scrollTimer.Stop();
                ModScroll.Offset = ModScroll.Offset.WithY(Math.Clamp(offset - 8, 0, MaxScroll(ModScroll)));
                _lastSetY = ModScroll.Offset.Y;
            }
        }, DispatcherPriority.Background);

    private void OnCardHeaderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source != sender || (sender as Control)?.DataContext is not ModItemViewModel card)
            return;

        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                card.IsExpanded = !card.IsExpanded;
                e.Handled = true;
                Dispatcher.UIThread.Post(() => BringIntoView(card), DispatcherPriority.Loaded);
                break;
            case Key.Down or Key.Up:
                MoveFocus(card, e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;
        }
    }

    private void OnCardHeaderGotFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (e.Source == sender && (sender as Control)?.DataContext is ModItemViewModel card)
            BringIntoView(card);
    }

    private void MoveFocus(ModItemViewModel from, int step)
    {
        if (_vm is null)
            return;

        var items = _vm.Items;
        var index = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], from))
            {
                index = i;
                break;
            }
        }

        do
            index += step;
        while (index >= 0 && index < items.Count && items[index] is not ModItemViewModel);

        if (index < 0 || index >= items.Count)
            return;

        var target = items[index];
        BringIntoView(target);
        // the container exists only after layout
        Dispatcher.UIThread.Post(() =>
            ModList.ContainerFor(target)?
                .GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(c => c.Classes.Contains("cardheader"))?
                .Focus(NavigationMethod.Directional),
            DispatcherPriority.Loaded);
    }

    private void BringIntoView(object item)
    {
        if (ModList.OffsetOf(item) is not { } top)
            return;

        const double Margin = 4;
        var height = ModList.ContainerFor(item)?.Bounds.Height ?? 44;
        var y = ModScroll.Offset.Y;
        if (top - Margin < y)
            y = top - Margin;
        else if (top + height + Margin > y + ModScroll.Viewport.Height)
            y = Math.Min(top - Margin, top + height + Margin - ModScroll.Viewport.Height);
        else
            return;

        _scrollTimer.Stop();
        ModScroll.Offset = ModScroll.Offset.WithY(Math.Clamp(y, 0, MaxScroll(ModScroll)));
    }

    private void OnCardHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (e.Source is Visual s && s.FindAncestorOfType<Avalonia.Controls.Primitives.ToggleButton>(includeSelf: true) is not null)
            return;

        if ((sender as Control)?.DataContext is ModItemViewModel card)
        {
            card.IsExpanded = !card.IsExpanded;
            e.Handled = true;
        }
    }
}
