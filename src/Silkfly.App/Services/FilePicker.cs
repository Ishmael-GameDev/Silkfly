using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Silkfly.App.Services;

public interface IFilePicker
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> types);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, FilePickerFileType type);
}

public sealed class FilePicker : IFilePicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (TopLevel.GetTopLevel(MainWindow)?.StorageProvider is not { CanPickFolder: true } storage)
            return null;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> types)
    {
        if (TopLevel.GetTopLevel(MainWindow)?.StorageProvider is not { CanOpen: true } storage)
            return null;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = types });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, FilePickerFileType type)
    {
        if (TopLevel.GetTopLevel(MainWindow)?.StorageProvider is not { CanSave: true } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = [type],
            DefaultExtension = type.Patterns?.FirstOrDefault()?.TrimStart('*', '.'),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    private static Window? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
