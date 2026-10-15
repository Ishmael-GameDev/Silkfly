using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Tests.Settings;

public sealed class SettingsServiceTests
{
    private static readonly string DataRoot = Path.Combine(Path.GetTempPath(), "silkfly-tests");

    private readonly MockFileSystem _fs = new();
    private readonly AppPaths _paths = new(DataRoot);

    private SettingsService CreateService() =>
        new(_fs, _paths, TimeProvider.System, NullLogger<SettingsService>.Instance);

    [Fact]
    public void Load_without_file_uses_defaults()
    {
        var service = CreateService();

        service.Load();

        service.Current.ShouldBe(new AppSettings());
        service.CorruptFileBackupPath.ShouldBeNull();
    }

    [Fact]
    public async Task Update_writes_file_that_loads_back()
    {
        var service = CreateService();
        service.Load();
        var profileId = Guid.NewGuid();

        await service.UpdateAsync(s => s with
        {
            Language = "ru",
            LaunchMode = LaunchMode.ViaSteam,
            ActiveProfileId = profileId,
            Window = new WindowPlacement(10, 20, 1200, 760, Maximized: true),
        });

        var reloaded = CreateService();
        reloaded.Load();
        reloaded.Current.ShouldBe(service.Current);
        reloaded.Current.Language.ShouldBe("ru");
        reloaded.Current.ActiveProfileId.ShouldBe(profileId);
        _fs.File.Exists(_paths.SettingsFile + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task Update_serializes_enums_as_strings_in_camel_case()
    {
        var service = CreateService();
        service.Load();

        await service.UpdateAsync(s => s with { LaunchMode = LaunchMode.ProtonConfig });

        var json = _fs.File.ReadAllText(_paths.SettingsFile);
        json.ShouldContain("\"launchMode\": \"ProtonConfig\"");
        json.ShouldContain("\"schemaVersion\": 1");
    }

    [Fact]
    public async Task Update_raises_changed_only_when_something_changed()
    {
        var service = CreateService();
        service.Load();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.UpdateAsync(s => s);
        await service.UpdateAsync(s => s with { UiScale = 1.1 });

        raised.ShouldBe(1);
    }

    [Fact]
    public void Load_with_corrupt_file_moves_it_aside_and_uses_defaults()
    {
        _fs.AddFile(_paths.SettingsFile, new MockFileData("{ this is not json"));
        var service = CreateService();

        service.Load();

        service.Current.ShouldBe(new AppSettings());
        service.CorruptFileBackupPath.ShouldNotBeNull();
        service.CorruptFileBackupPath.ShouldStartWith(_paths.SettingsFile + ".bad-");
        _fs.File.Exists(service.CorruptFileBackupPath).ShouldBeTrue();
        _fs.File.Exists(_paths.SettingsFile).ShouldBeFalse();
    }

    [Fact]
    public void Load_ignores_unknown_properties()
    {
        _fs.AddFile(_paths.SettingsFile, new MockFileData("""{ "language": "en", "someFutureSetting": 42 }"""));
        var service = CreateService();

        service.Load();

        service.Current.Language.ShouldBe("en");
        service.CorruptFileBackupPath.ShouldBeNull();
    }

    [Fact]
    public void Missing_properties_keep_their_defaults()
    {
        _fs.AddFile(_paths.SettingsFile, new MockFileData("""{ "language": "ru" }"""));
        var service = CreateService();

        service.Load();

        service.Current.UiScale.ShouldBe(1.0);
        service.Current.WhatsNewDays.ShouldBe(7);
        service.Current.CheckForUpdates.ShouldBeTrue();
        service.Current.UpdateChannel.ShouldBe("stable");
    }

    [Fact]
    public void Out_of_range_values_are_repaired()
    {
        _fs.AddFile(_paths.SettingsFile, new MockFileData("""{ "uiScale": 0, "whatsNewDays": 0 }"""));
        var service = CreateService();

        service.Load();

        service.Current.UiScale.ShouldBe(1.0);
        service.Current.WhatsNewDays.ShouldBe(7);
    }
}