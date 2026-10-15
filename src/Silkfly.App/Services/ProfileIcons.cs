using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Profiles;

namespace Silkfly.App.Services;

public sealed class ProfileIcons(
    IProfileService profiles,
    IFilePicker picker,
    IToastService toasts,
    ILocalizer localizer,
    ILogger<ProfileIcons> logger)
{
    private const int DecodeWidth = 64;

    private static readonly ConcurrentDictionary<(string Path, DateTime Written), Bitmap> Cache = new();

    public async Task ChooseAsync(Profile profile)
    {
        var type = new FilePickerFileType(localizer["Profiles_Icon_Images"])
        {
            Patterns = ProfileService.IconExtensions.Select(e => "*" + e).ToList(),
        };
        var file = await picker.PickFileAsync(localizer["Profiles_Icon_Pick"], [type]);
        if (file is null)
            return;

        try
        {
            await profiles.SetIconAsync(profile, file, CancellationToken.None);
        }
        catch (Exception ex) when (ex is SilkflyException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Setting the icon of {Profile} failed", profile.Name);
            toasts.ShowError("Profiles_Icon_Failed", ex);
        }
    }

    public async Task RemoveAsync(Profile profile)
    {
        try
        {
            await profiles.SetIconAsync(profile, null, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            toasts.ShowError("Profiles_Icon_Failed", ex);
        }
    }

    public async Task<Bitmap?> LoadAsync(Profile profile)
    {
        if (profile.IconPath is not { } path || !File.Exists(path))
            return null;

        var key = (path, File.GetLastWriteTimeUtc(path));
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, DecodeWidth, BitmapInterpolationMode.HighQuality);
            });
            Cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            logger.LogInformation("Profile icon {Path} is unreadable: {Error}", path, ex.Message);
            return null;
        }
    }
}
