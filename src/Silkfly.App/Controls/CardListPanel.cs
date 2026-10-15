using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Silkfly.App.Controls;

public interface IExpandableItem
{
    bool IsExpanded { get; }
}

// Virtualizing card list with exact heights, so nothing shifts while scrolling.
public sealed class CardListPanel : Panel
{
    public static readonly StyledProperty<IReadOnlyList<object>?> ItemsProperty =
        AvaloniaProperty.Register<CardListPanel, IReadOnlyList<object>?>(nameof(Items));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardListPanel, double>(nameof(Spacing), 6);

    private const double Overscan = 500;
    private const double DefaultCollapsedHeight = 44;
    private const int MaxRetries = 3;

    private readonly Dictionary<object, ContentControl> _realized = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (bool Expanded, double Height)> _heights = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _indexOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _retries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, Stack<ContentControl>> _pool = new();
    private double[] _offsets = [];
    private double _collapsedHeight = double.NaN;
    private double _width = double.NaN;
    private Rect _viewport;

    static CardListPanel()
    {
        AffectsMeasure<CardListPanel>(ItemsProperty, SpacingProperty);
    }

    public CardListPanel()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            InvalidateMeasure();
        };
    }

    public IReadOnlyList<object>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public Control? ContainerFor(object item) => _realized.GetValueOrDefault(item);

    public double? OffsetOf(object item) =>
        _indexOf.TryGetValue(item, out var index) && index < _offsets.Length ? _offsets[index] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items ?? [];
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        if (!width.Equals(_width))
        {
            // text wraps differently now
            foreach (var item in _heights.Where(h => h.Value.Expanded).Select(h => h.Key).ToList())
                _heights.Remove(item);
            _width = width;
        }

        _indexOf.Clear();
        for (var i = 0; i < items.Count; i++)
            _indexOf[items[i]] = i;
        if (_heights.Count > items.Count * 2)
        {
            foreach (var stale in _heights.Keys.Where(k => !_indexOf.ContainsKey(k)).ToList())
                _heights.Remove(stale);
        }

        var keep = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var constraint = new Size(width, double.PositiveInfinity);

        // measure expanded cards even off screen
        foreach (var item in items)
        {
            if (IsExpanded(item) && !HasExactHeight(item))
                MeasureItem(item, constraint);
        }

        var total = ComputeOffsets(items);
        var viewportHeight = _viewport.Height > 0 ? _viewport.Height : 1000;
        // the list may have got shorter than the scroll position
        var viewTop = Math.Clamp(_viewport.Y, 0, Math.Max(0, total - viewportHeight));
        var top = viewTop - Overscan;
        var bottom = viewTop + viewportHeight + Overscan;

        for (var i = 0; i < items.Count; i++)
        {
            if (_offsets[i] > bottom)
                break;
            if (_offsets[i] + HeightOf(items[i]) < top)
                continue;

            MeasureItem(items[i], constraint);
            keep.Add(items[i]);
        }

        // keep the focused card realized
        foreach (var (item, container) in _realized)
        {
            if (container.IsKeyboardFocusWithin && _indexOf.ContainsKey(item))
                keep.Add(item);
        }

        foreach (var item in _realized.Keys.Where(k => !keep.Contains(k)).ToList())
            Recycle(item);

        total = ComputeOffsets(items);
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (item, container) in _realized)
        {
            if (_indexOf.TryGetValue(item, out var index) && index < _offsets.Length)
                container.Arrange(new Rect(0, _offsets[index], finalSize.Width, container.DesiredSize.Height));
        }

        foreach (var spare in _pool.Values.SelectMany(s => s))
            spare.Arrange(default);

        return finalSize;
    }

    private void MeasureItem(object item, Size constraint)
    {
        var container = Realize(item);
        container.Measure(constraint);
        var height = container.DesiredSize.Height;
        if (height <= 0)
            return;

        var expanded = IsExpanded(item);
        if (expanded && height <= CollapsedHeight + 1 && _retries.GetValueOrDefault(item) < MaxRetries)
        {
            // a reused container may still show the collapsed card: try again next frame
            _retries[item] = _retries.GetValueOrDefault(item) + 1;
            Dispatcher.UIThread.Post(InvalidateMeasure, DispatcherPriority.Render);
            return;
        }

        _retries.Remove(item);
        _heights[item] = (expanded, height);
        // smallest one, so a card caught mid-collapse can't inflate it
        if (!expanded && (double.IsNaN(_collapsedHeight) || height < _collapsedHeight))
            _collapsedHeight = height;
    }

    private double ComputeOffsets(IReadOnlyList<object> items)
    {
        if (_offsets.Length != items.Count)
            _offsets = new double[items.Count];

        var y = 0.0;
        var spacing = Spacing;
        for (var i = 0; i < items.Count; i++)
        {
            _offsets[i] = y;
            y += HeightOf(items[i]) + spacing;
        }

        return items.Count > 0 ? y - spacing : 0;
    }

    private bool HasExactHeight(object item) =>
        _heights.TryGetValue(item, out var known) && known.Expanded == IsExpanded(item);

    private double HeightOf(object item)
    {
        if (_heights.TryGetValue(item, out var known) && known.Expanded == IsExpanded(item))
            return known.Height;
        return CollapsedHeight;
    }

    private double CollapsedHeight => double.IsNaN(_collapsedHeight) ? DefaultCollapsedHeight : _collapsedHeight;

    private static bool IsExpanded(object item) => item is IExpandableItem { IsExpanded: true };

    private ContentControl Realize(object item)
    {
        if (_realized.TryGetValue(item, out var existing))
            return existing;

        if (!(_pool.TryGetValue(item.GetType(), out var spares) && spares.TryPop(out var container)))
        {
            container = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            Children.Add(container);
        }

        container.Content = item;
        container.IsVisible = true;
        container.ApplyTemplate();
        container.Presenter?.UpdateChild();
        _realized[item] = container;
        return container;
    }

    private void Recycle(object item)
    {
        if (!_realized.Remove(item, out var container))
            return;

        container.IsVisible = false;
        var type = item.GetType();
        if (!_pool.TryGetValue(type, out var spares))
            _pool[type] = spares = new Stack<ContentControl>();
        spares.Push(container);
    }
}
