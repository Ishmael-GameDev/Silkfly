using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;

namespace Silkfly.Core.Catalog;

public sealed class CatalogService(
    IThunderstoreClient client,
    CatalogCache cache,
    TimeProvider time,
    ILogger<CatalogService> logger) : ICatalogService
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile CatalogSnapshot _current = CatalogSnapshot.Empty;

    public CatalogSnapshot Current => _current;

    public bool IsRefreshing { get; private set; }

    public Exception? LastRefreshError { get; private set; }

    public event EventHandler<CatalogSnapshot>? CatalogChanged;

    public event EventHandler? RefreshStateChanged;

    public async Task LoadCachedAsync(CancellationToken ct)
    {
        var cached = await cache.LoadAsync(ct).ConfigureAwait(false);
        if (cached is null || _current.Source == CatalogSource.Network)
            return;

        logger.LogInformation("Loaded {Count} packages from catalog cache ({FetchedAt})", cached.Packages.Count, cached.FetchedAt);
        Replace(cached);
    }

    public async Task RefreshAsync(bool force, CancellationToken ct)
    {
        if (!force && _current.Source != CatalogSource.Empty && time.GetUtcNow() - _current.FetchedAt < Ttl)
            return;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SetRefreshState(true, null);
            var dtos = await client.FetchCommunityPackagesAsync(CatalogRules.Community, null, ct).ConfigureAwait(false);
            var packages = CatalogMapper.Map(dtos, logger);
            var snapshot = CatalogSnapshot.Create(packages, time.GetUtcNow(), CatalogSource.Network);
            logger.LogInformation("Fetched {Count} packages from Thunderstore", packages.Count);

            try
            {
                await cache.SaveAsync(snapshot, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not write the catalog cache");
            }

            Replace(snapshot);
            SetRefreshState(false, null);
        }
        catch (OperationCanceledException)
        {
            SetRefreshState(false, LastRefreshError);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catalog refresh failed");
            SetRefreshState(false, ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Package? Find(PackageId id) => _current.ById.GetValueOrDefault(id);

    public PackageVersion? FindVersion(PackageId id, SemVersion version) => Find(id)?.FindVersion(version);

    public IReadOnlyList<Package> Query(CatalogQuery query) => CatalogSearch.Query(_current.Listable, query);

    private void Replace(CatalogSnapshot snapshot)
    {
        _current = snapshot;
        CatalogChanged?.Invoke(this, snapshot);
    }

    private void SetRefreshState(bool refreshing, Exception? error)
    {
        IsRefreshing = refreshing;
        LastRefreshError = error;
        RefreshStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
