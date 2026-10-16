using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Semver;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Install;
using Silkfly.Core.Models;
using Silkfly.Core.Thunderstore;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Silkfly.Core.Profiles;

public sealed record ExportedMod(PackageId Id, SemVersion Version, bool Enabled);

public sealed record ProfileImport(string ProfileName, IReadOnlyList<ExportedMod> Mods, IReadOnlyDictionary<string, byte[]> Configs)
{
    public ProfileIconData? Icon { get; init; }
}

public sealed record ProfileIconData(string Extension, byte[] Content);

public sealed record ExportResult(int ModCount, int ConfigCount, IReadOnlyList<PackageId> SkippedManual);

public interface IProfileTransfer
{
    Task<ExportResult> ExportR2zAsync(Profile profile, string destinationFile, CancellationToken ct);

    Task<ProfileImport> ReadR2zAsync(string file, CancellationToken ct);

    Task<Profile> CreateFromImportAsync(ProfileImport import, string name, CancellationToken ct);

    Task<string> ExportCodeAsync(Profile profile, CancellationToken ct);

    Task<ProfileImport> ReadCodeAsync(string code, CancellationToken ct);
}

public sealed class ProfileTransfer(
    IFileSystem fs,
    IProfileService profiles,
    IThunderstoreClient thunderstore,
    ILogger<ProfileTransfer> logger) : IProfileTransfer
{
    public const string ManifestName = "export.r2x";
    private const string ConfigPrefix = "BepInEx/config/";

    private static readonly ISerializer Yaml = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer YamlReader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async Task<ExportResult> ExportR2zAsync(Profile profile, string destinationFile, CancellationToken ct)
    {
        var bytes = BuildExport(profile, out var result);
        if (fs.Path.GetDirectoryName(destinationFile) is { Length: > 0 } folder)
            fs.Directory.CreateDirectory(folder);
        await fs.File.WriteAllBytesAsync(destinationFile, bytes, ct).ConfigureAwait(false);
        logger.LogInformation("Exported profile {Profile} to {File}: {Mods} mods, {Configs} configs", profile.Name, destinationFile, result.ModCount, result.ConfigCount);
        return result;
    }

    public async Task<ProfileImport> ReadR2zAsync(string file, CancellationToken ct) =>
        Parse(await fs.File.ReadAllBytesAsync(file, ct).ConfigureAwait(false));

    public async Task<Profile> CreateFromImportAsync(ProfileImport import, string name, CancellationToken ct)
    {
        var profile = await profiles.CreateAsync(name, ct).ConfigureAwait(false);
        foreach (var (relative, content) in import.Configs)
        {
            var target = SafeZip.ResolveInside(fs, profile.ConfigPath, relative);
            fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(target)!);
            await fs.File.WriteAllBytesAsync(target, content, ct).ConfigureAwait(false);
        }

        if (import.Icon is { } icon)
        {
            var temp = fs.Path.Combine(profile.RootPath, ".import-icon" + icon.Extension);
            await fs.File.WriteAllBytesAsync(temp, icon.Content, ct).ConfigureAwait(false);
            try
            {
                profile = await profiles.SetIconAsync(profile, temp, ct).ConfigureAwait(false);
            }
            catch (SilkflyException ex)
            {
                logger.LogWarning("Imported profile icon skipped: {Error}", ex.MessageKey);
            }
            finally
            {
                fs.File.Delete(temp);
            }
        }

        return profile;
    }

    public async Task<string> ExportCodeAsync(Profile profile, CancellationToken ct) =>
        await thunderstore.CreateProfileCodeAsync(BuildExport(profile, out _), ct).ConfigureAwait(false);

    public async Task<ProfileImport> ReadCodeAsync(string code, CancellationToken ct) =>
        Parse(await thunderstore.GetProfileByCodeAsync(code.Trim(), ct).ConfigureAwait(false));

    private byte[] BuildExport(Profile profile, out ExportResult result)
    {
        var lockFile = profiles.GetLock(profile);
        var exported = lockFile.Mods.Where(m => m.Source != ModSource.ManualDll && m.Id.Namespace != "manual").ToList();
        var skipped = lockFile.Mods.Except(exported).Select(m => m.Id).ToList();

        var manifest = new R2xManifest
        {
            ProfileName = profile.Name,
            Mods = exported.Select(m => new R2xMod
            {
                Name = m.Id.FullName,
                Version = new R2xVersion { Major = (int)m.Version.Major, Minor = (int)m.Version.Minor, Patch = (int)m.Version.Patch },
                Enabled = m.Enabled,
            }).ToList(),
        };

        using var buffer = new MemoryStream();
        var configs = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(ManifestName);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(Yaml.Serialize(manifest));

            if (fs.Directory.Exists(profile.ConfigPath))
            {
                foreach (var file in fs.Directory.EnumerateFiles(profile.ConfigPath, "*", SearchOption.AllDirectories))
                {
                    var relative = fs.Path.GetRelativePath(profile.ConfigPath, file).Replace('\\', '/');
                    var configEntry = zip.CreateEntry(ConfigPrefix + relative);
                    using var output = configEntry.Open();
                    using var input = fs.File.OpenRead(file);
                    input.CopyTo(output);
                    configs++;
                }
            }

            if (profile.IconPath is { } iconPath && fs.File.Exists(iconPath) && fs.FileInfo.New(iconPath).Length <= ProfileService.MaxIconBytes)
            {
                using var output = zip.CreateEntry(fs.Path.GetFileName(iconPath)).Open();
                using var input = fs.File.OpenRead(iconPath);
                input.CopyTo(output);
            }
        }

        result = new ExportResult(exported.Count, configs, skipped);
        return buffer.ToArray();
    }

    internal static ProfileImport Parse(byte[] r2z)
    {
        using var zip = OpenZip(r2z);
        SafeZip.Validate(zip);

        var manifestEntry = zip.Entries.FirstOrDefault(e => SafeZip.Normalize(e.FullName).Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new SilkflyException("Error_ImportNoManifest");

        R2xManifest manifest;
        try
        {
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            manifest = YamlReader.Deserialize<R2xManifest>(reader) ?? throw new SilkflyException("Error_ImportNoManifest");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new SilkflyException("Error_ImportBadManifest", ex);
        }

        var mods = new List<ExportedMod>();
        foreach (var mod in manifest.Mods ?? [])
        {
            if (!PackageId.TryParseFullName(mod.Name, out var id) || mod.Version is null)
                continue;
            mods.Add(new ExportedMod(id, new SemVersion(mod.Version.Major, mod.Version.Minor, mod.Version.Patch), mod.Enabled));
        }

        var configs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ProfileIconData? icon = null;
        foreach (var entry in zip.Entries)
        {
            var path = SafeZip.Normalize(entry.FullName);
            if (path.EndsWith('/'))
                continue;

            if (icon is null && IconExtension(path) is { } extension && entry.Length <= ProfileService.MaxIconBytes)
            {
                icon = new ProfileIconData(extension, Read(entry));
                continue;
            }

            string? relative = path.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase) ? path[ConfigPrefix.Length..]
                : path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ? path["config/".Length..]
                : null;
            if (relative is null || relative.Length == 0)
                continue;

            configs[relative] = Read(entry);
        }

        return new ProfileImport(string.IsNullOrWhiteSpace(manifest.ProfileName) ? "Imported" : manifest.ProfileName!, mods, configs)
        {
            Icon = icon,
        };
    }

    private static string? IconExtension(string path) =>
        ProfileService.IconExtensions.FirstOrDefault(e => path.Equals("icon" + e, StringComparison.OrdinalIgnoreCase));

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return copy.ToArray();
    }

    private static ZipArchive OpenZip(byte[] bytes)
    {
        try
        {
            return new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new SilkflyException("Error_BadArchive", ex, "r2z");
        }
    }

    internal sealed class R2xManifest
    {
        public string? ProfileName { get; set; }
        public List<R2xMod>? Mods { get; set; }
    }

    internal sealed class R2xMod
    {
        public string? Name { get; set; }
        public R2xVersion? Version { get; set; }
        public bool Enabled { get; set; } = true;
    }

    internal sealed class R2xVersion
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
    }
}
