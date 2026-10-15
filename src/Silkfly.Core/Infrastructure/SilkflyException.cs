namespace Silkfly.Core.Infrastructure;

public class SilkflyException : Exception
{
    public SilkflyException(string messageKey, params object[] args)
        : this(messageKey, null, args)
    {
    }

    public SilkflyException(string messageKey, Exception? inner, params object[] args)
        : base(messageKey, inner)
    {
        MessageKey = messageKey;
        Args = args;
    }

    public string MessageKey { get; }

    public IReadOnlyList<object> Args { get; }
}

public class NetworkException(string messageKey, Exception? inner = null, params object[] args)
    : SilkflyException(messageKey, inner, args);

public sealed class ThunderstoreUnavailableException(Exception? inner = null)
    : NetworkException("Error_ThunderstoreUnavailable", inner);

public sealed class ThunderstoreFormatException(Exception? inner = null)
    : SilkflyException("Error_ThunderstoreFormat", inner);

public sealed class GameNotFoundException()
    : SilkflyException("Error_GameNotFound");

public sealed class GameRunningException()
    : SilkflyException("Error_GameRunning");

public sealed class LoaderMissingException()
    : SilkflyException("Error_LoaderMissing");

public sealed class InstallConflictException(string path)
    : SilkflyException("Error_InstallConflict", path);

public sealed class UnsafeArchiveException(string entry)
    : SilkflyException("Error_UnsafeArchive", entry);

public sealed class ProfileCorruptException(string profileName, Exception? inner = null)
    : SilkflyException("Error_ProfileCorrupt", inner, profileName);

public sealed class InsufficientPermissionsException(string path, Exception? inner = null)
    : SilkflyException("Error_InsufficientPermissions", inner, path);

public sealed class PartialImportException(IReadOnlyList<string> missing)
    : SilkflyException("Error_ImportMissingMods", string.Join(", ", missing))
{
    public IReadOnlyList<string> Missing { get; } = missing;
}
