using CommunityToolkit.Mvvm.ComponentModel;

namespace Silkfly.App.Services;

public sealed partial class UiScaleLimits : ObservableObject
{
    public const double Min = 0.9;
    public const double Max = 1.3;

    public const double BaseMinWidth = 850;
    public const double BaseMinHeight = 500;

    [ObservableProperty]
    private double _screenMax = Max;

    public double Clamp(double scale) => Math.Clamp(scale, Min, Math.Max(Min, Math.Min(Max, ScreenMax)));

    public void UpdateScreen(double width, double height) =>
        ScreenMax = Math.Floor(Math.Min(width / BaseMinWidth, height / BaseMinHeight) * 100) / 100;
}
