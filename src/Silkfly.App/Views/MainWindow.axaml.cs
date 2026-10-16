using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Silkfly.App.Localization;
using Silkfly.App.Services;
using Silkfly.App.ViewModels;
using Silkfly.Core.Settings;

namespace Silkfly.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly ISettingsService? _settings;
    private readonly UiScaleLimits _limits = new();
    private double _appliedScale = 1;
    private bool _placementSaved;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ISettingsService settings, UiScaleLimits limits)
        : this()
    {
        _settings = settings;
        _limits = limits;
        ConfigureChrome();
        RestorePlacement(settings.Current.Window);
        ApplyScale(settings.Current.UiScale, force: true);
        settings.Changed += (_, s) => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyScale(s.UiScale));

        Opened += (_, _) => UpdateScreenLimit();
        PositionChanged += (_, _) => UpdateScreenLimit();
        ScalingChanged += (_, _) => UpdateScreenLimit();
        using (var icon = AssetLoader.Open(new Uri("avares://Silkfly/Assets/silkfly.ico")))
            Icon = new WindowIcon(icon);
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void ApplyScale(double requested, bool force = false)
    {
        var scale = _limits.Clamp(requested);
        if (!force && Math.Abs(scale - _appliedScale) < 0.001)
            return;

        var ratio = scale / _appliedScale;
        _appliedScale = scale;
        Scaler.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(scale, scale);
        MinWidth = UiScaleLimits.BaseMinWidth * scale;
        MinHeight = UiScaleLimits.BaseMinHeight * scale;

        Scaler.InvalidateMeasure();
        if (WindowState != WindowState.Normal)
            return;

        // Width is NaN after maximize/restore: compare with the real size
        var currentWidth = double.IsNaN(Width) ? ClientSize.Width : Width;
        var currentHeight = double.IsNaN(Height) ? ClientSize.Height : Height;
        var width = Math.Max(force ? currentWidth : currentWidth * ratio, MinWidth);
        var height = Math.Max(force ? currentHeight : currentHeight * ratio, MinHeight);
        if (Math.Abs(width - currentWidth) > 0.5 || Math.Abs(height - currentHeight) > 0.5)
        {
            Width = width;
            Height = height;
            KeepOnScreen(width, height);
        }
    }

    private Screen? CurrentScreen => Screens.ScreenFromWindow(this) ?? Screens.Primary;

    private void UpdateScreenLimit()
    {
        if (CurrentScreen is not { } screen)
            return;

        var area = screen.WorkingArea;
        _limits.UpdateScreen(area.Width / screen.Scaling, area.Height / screen.Scaling);
        if (_settings is not null)
            ApplyScale(_settings.Current.UiScale);
    }

    private void KeepOnScreen(double width, double height)
    {
        if (CurrentScreen is not { } screen)
            return;

        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(new Size(width, height), screen.Scaling);
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - size.Width));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        if (x != Position.X || y != Position.Y)
            Position = new PixelPoint(x, y);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Shell is not { } shell || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        if (e.Key == Key.F)
        {
            shell.CurrentPage = ShellPage.Mods;
            Dispatcher.UIThread.Post(() => this.FindDescendantOfType<ModsView>()?.FocusSearch(), DispatcherPriority.Loaded);
            e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            shell.Mods.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ConfigureChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            Controls.WindowCorners.Apply(this, Outline);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            CaptionButtons.IsVisible = false;
            LogoPanel.Margin = new Thickness(84, 0, 24, 0);
        }
        else
        {
            CaptionButtons.IsVisible = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            UpdateMaximizedState();
    }

    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;

        // maximized + extended client area overflows the screen on Windows
        Root.Margin = maximized ? OffScreenMargin : default;

        MaximizeGlyph.Data = (Avalonia.Media.Geometry?)this.FindResource(maximized ? "Caption.Restore" : "Caption.Maximize");
        MaximizeButton.Bind(ToolTip.TipProperty, StrExtension.CreateBinding(maximized ? "Caption_Restore" : "Caption_Maximize"));
    }

    // no drag from controls: ComboBox doesn't handle the press and the drag eats its release
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsOnControl(e.Source))
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
            BeginMoveDrag(e);
    }

    // only up to the header: the window itself is a TemplatedControl too
    private bool IsOnControl(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != Header; visual = visual.GetVisualParent())
        {
            if (visual is Avalonia.Controls.Primitives.TemplatedControl)
                return true;
        }

        return false;
    }

    private void OnHeaderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsOnControl(e.Source))
            return;

        ToggleMaximize();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null
            || placement.Width < UiScaleLimits.BaseMinWidth * UiScaleLimits.Min
            || placement.Height < UiScaleLimits.BaseMinHeight * UiScaleLimits.Min)
            return;

        var position = new PixelPoint(placement.X, placement.Y);
        var onScreen = Screens.All.Any(s => s.WorkingArea.Contains(position + new PixelPoint(40, 20)));
        if (onScreen)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }

        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_settings is null || _placementSaved || e.Cancel)
            return;

        e.Cancel = true;
        _placementSaved = true;
        var maximized = WindowState == WindowState.Maximized;
        var placement = new WindowPlacement(Position.X, Position.Y, ClientSize.Width, ClientSize.Height, maximized);
        if (maximized && _settings.Current.Window is { } previous)
            placement = previous with { Maximized = true };

        try
        {
            await _settings.UpdateAsync(s => s with { Window = placement });
        }
        finally
        {
            Close();
        }
    }
}
