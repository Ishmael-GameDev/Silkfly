using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.Tests.Infrastructure;

public sealed class AppPathsTests
{
    private static readonly string DataRoot = Path.Combine(Path.GetTempPath(), "silkfly-data");

    [Fact]
    public void Default_folders_live_under_data_root()
    {
        var paths = new AppPaths(DataRoot);

        paths.CacheRoot.ShouldBe(Path.Combine(DataRoot, "cache"));
        paths.LogsRoot.ShouldBe(Path.Combine(DataRoot, "logs"));
        paths.ProfilesRoot.ShouldBe(Path.Combine(DataRoot, "profiles"));
        paths.SettingsFile.ShouldBe(Path.Combine(DataRoot, "settings.json"));
        paths.LoaderCacheRoot.ShouldBe(Path.Combine(DataRoot, "loader"));
    }

    [Fact]
    public void Profiles_root_follows_the_override_when_set()
    {
        string? custom = null;
        var paths = new AppPaths(DataRoot, () => custom);

        paths.ProfilesRoot.ShouldBe(Path.Combine(DataRoot, "profiles"));

        custom = Path.Combine(Path.GetTempPath(), "my-profiles");
        paths.ProfilesRoot.ShouldBe(custom);
    }
}
