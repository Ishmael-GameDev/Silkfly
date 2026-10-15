using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Silkfly.App.Views;

public sealed partial class InfoView : UserControl
{
    private const double AmplitudeY = 7;
    private const double AmplitudeX = 4;
    private const double PeriodSeconds = 18;

    // 20 fps is enough for this slow drift
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly TranslateTransform _drift = new();
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.UtcNow;

    public InfoView()
    {
        InitializeComponent();
        Flies.RenderTransform = _drift;
        _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, (_, _) => Step());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    private void Step()
    {
        var phase = (DateTime.UtcNow - _start).TotalSeconds / PeriodSeconds * 2 * Math.PI;
        _drift.X = Math.Sin(phase) * AmplitudeX;
        _drift.Y = Math.Sin(phase * 2) * AmplitudeY;
    }
}
