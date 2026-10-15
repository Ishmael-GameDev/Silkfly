using Semver;
using Silkfly.Core.Models;

namespace Silkfly.Core.Catalog;

public interface ICatalogService
{
    CatalogSnapshot Current { get; }

    bool IsRefreshing { get; }

    Exception? LastRefreshError { get; }

    event EventHandler<CatalogSnapshot>? CatalogChanged;

    event EventHandler? RefreshStateChanged;

    Task LoadCachedAsync(CancellationToken ct);

    Task RefreshAsync(bool force, CancellationToken ct);

    Package? Find(PackageId id);

    PackageVersion? FindVersion(PackageId id, SemVersion version);

    IReadOnlyList<Package> Query(CatalogQuery query);
}
