using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.App.Services;

public interface IIconCache
{
    Task<Bitmap?> GetAsync(PackageVersion version, CancellationToken ct = default);
}

public sealed class IconCache(IHttpClientFactory httpFactory, IAppPaths paths, ILogger<IconCache> logger) : IIconCache
{
    private const int Size = 128;
    private readonly SemaphoreSlim _downloads = new(6, 6);

    public async Task<Bitmap?> GetAsync(PackageVersion version, CancellationToken ct = default)
    {
        if (version.IconUrl is null)
            return null;

        var folder = Path.Combine(paths.CacheRoot, "icons", version.PackageId.FullName);
        var file = Path.Combine(folder, $"{version.Version}-{Size}.png");

        try
        {
            if (File.Exists(file))
                return await Task.Run(() => new Bitmap(file), ct).ConfigureAwait(false);

            await _downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThunderstoreClient.EnsureTrustedHost(version.IconUrl);
                var client = httpFactory.CreateClient(ThunderstoreClient.HttpClientName);
                var bytes = await client.GetByteArrayAsync(version.IconUrl, ct).ConfigureAwait(false);

                return await Task.Run(() =>
                {
                    using var input = new MemoryStream(bytes);
                    var bitmap = Bitmap.DecodeToWidth(input, Size);

                    if (Directory.Exists(folder))
                    {
                        foreach (var old in Directory.EnumerateFiles(folder, "*.png"))
                            File.Delete(old);
                    }

                    Directory.CreateDirectory(folder);
                    bitmap.Save(file);
                    return bitmap;
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                _downloads.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No icon for {Package} {Version}", version.PackageId, version.Version);
            return null;
        }
    }
}
