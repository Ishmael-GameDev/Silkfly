namespace Silkfly.Core.Updates;

public sealed record AppUpdateInfo(string Version, string? NotesMarkdown);

public interface IAppUpdateService
{
    bool IsSupported { get; }

    Task<AppUpdateInfo?> CheckAsync(CancellationToken ct);

    Task ApplyAndRestartAsync(IProgress<double>? progress, CancellationToken ct);
}

public static class UpdateChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";
}

public static class UpdateSchedule
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(6);

    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) =>
        lastCheck is not { } last || last > now || now - last >= MinInterval;
}
