using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Silkfly.App.Controls;

public sealed class GlyphIcon : Control
{
    private const double GridSize = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<GlyphIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<GlyphIcon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<GlyphIcon, double>(nameof(StrokeThickness), 2);

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<GlyphIcon, bool>(nameof(IsFilled));

    static GlyphIcon()
    {
        AffectsRender<GlyphIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty, IsFilledProperty);
        WidthProperty.OverrideDefaultValue<GlyphIcon>(18);
        HeightProperty.OverrideDefaultValue<GlyphIcon>(18);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = Math.Min(Bounds.Width, Bounds.Height) / GridSize;
        var offsetX = (Bounds.Width - GridSize * scale) / 2;
        var offsetY = (Bounds.Height - GridSize * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (IsFilled)
            {
                context.DrawGeometry(Foreground, null, Data);
            }
            else
            {
                var pen = new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, Data);
            }
        }
    }
}
