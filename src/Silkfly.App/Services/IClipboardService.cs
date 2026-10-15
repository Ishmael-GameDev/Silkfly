using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Silkfly.App.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (TopLevel.GetTopLevel(window)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
