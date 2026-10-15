using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Silkfly.App.Controls;

public interface IExpandableItem
{
    bool IsExpanded { get; }
}

// Virtualizing card list. Heights are exact (collapsed size + measured expanded ones), so scrolling never jumps.
public sealed class CardListPanel : Panel
{
    public static readonly StyledProperty<IReadOnlyList<object>?> ItemsProperty =
        AvaloniaProperty.Register<CardListPanel, IReadOnlyList<object>?>(nameof(Items));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CardListPanel, double>(nameof(Spacing), 6);

    private const double Overscan = 500;

    private readonly Dictionary<object, ContentControl> _realized = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, double> _expandedHeights = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _indexOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, Stack<ContentControl>> _pool = new();
    private double[] _offsets = [];
    private double _collapsedHeight = 44;
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            foreach (var item in _realized.Keys.ToList())
                Recycle(item);
            _expandedHeights.Clear();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items ?? [];
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        if (!width.Equals(_width))
        {
            _expandedHeights.Clear();
            _width = width;
        }

        _indexOf.Clear();
        for (var i = 0; i < items.Count; i++)
            _indexOf[items[i]] = i;

        ComputeOffsets(items);
        var top = _viewport.Y - Overscan;
        var bottom = (_viewport.Height > 0 ? _viewport.Bottom : 1000) + Overscan;

        var keep = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < items.Count; i++)
        {
            var itemTop = _offsets[i];
            if (itemTop > bottom)
                break;
            if (itemTop + HeightOf(items[i]) < top)
                continue;

            var item = items[i];
            var container = Realize(item);
            container.Measure(new Size(width, double.PositiveInfinity));
            var height = container.DesiredSize.Height;
            if (IsExpanded(item))
                _expandedHeights[item] = height;
            else if (height > 0)
                _collapsedHeight = height;
            keep.Add(item);
        }

        // keep the focused card realized
        foreach (var (item, container) in _realized)
        {
            if (container.IsKeyboardFocusWithin && _indexOf.ContainsKey(item))
                keep.Add(item);
        }

        foreach (var item in _realized.Keys.Where(k => !keep.Contains(k)).ToList())
            Recycle(item);

        var total = ComputeOffsets(items);
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

    private double HeightOf(object item) =>
        IsExpanded(item) && _expandedHeights.TryGetValue(item, out var height) ? height : _collapsedHeight;

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
