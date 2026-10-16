using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Silkfly.Core.Game;
using Silkfly.Core.Infrastructure;
using Silkfly.Core.Loader;
using Silkfly.Core.Profiles;
using Silkfly.Core.Settings;

namespace Silkfly.Core.Launch;

public sealed record LaunchResult(bool Started, int? ProcessId, string? ErrorKey, object[]? ErrorArgs = null)
{
    public static LaunchResult Fail(string key, params object[] args) => new(false, null, key, args);
}

public interface ILaunchService
{
    Task<LaunchResult> LaunchModdedAsync(Profile profile, CancellationToken ct);

    Task<LaunchResult> LaunchVanillaAsync(CancellationToken ct);

    void AttachToRunningGame();

    bool IsGameRunning { get; }

    Profile? RunningProfile { get; }

    event EventHandler? RunningChanged;
}

public interface IProcessRunner
{
    Process? Start(ProcessStartInfo info);

    IReadOnlyList<Process> FindByName(string name);
}

public sealed class ProcessRunner : IProcessRunner
{
    public Process? Start(ProcessStartInfo info) => Process.Start(info);

    public IReadOnlyList<Process> FindByName(string name) => Process.GetProcessesByName(name);
}

public sealed class LaunchService(
    System.IO.Abstractions.IFileSystem fs,
    ISettingsService settings,
    IGameLocator locator,
    ILoaderService loader,
    IProfileService profiles,
    ISystemEnvironment env,
    IProcessRunner processes,
    ILogger<LaunchService> logger) : ILaunchService
{
    private static readonly TimeSpan SteamAppearTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _watch;

    public bool IsGameRunning { get; private set; }

    public Profile? RunningProfile { get; private set; }

    public event EventHandler? RunningChanged;

    public Task<LaunchResult> LaunchModdedAsync(Profile profile, CancellationToken ct) => LaunchAsync(profile, ct);

    public Task<LaunchResult> LaunchVanillaAsync(CancellationToken ct) => LaunchAsync(null, ct);

    private async Task<LaunchResult> LaunchAsync(Profile? profile, CancellationToken ct)
    {
        if (!IsGameRunning)
            AttachToRunningGame();
        if (IsGameRunning)
            return LaunchResult.Fail("Error_GameAlreadyRunning");

        if (settings.Current.GamePath is not { } gamePath || locator.Validate(gamePath).Install is not { } game)
            return LaunchResult.Fail("Error_GameNotFound");

        if (profile is not null)
        {
            var status = await loader.GetStatusAsync(game, ct).ConfigureAwait(false);
            if (!status.IsInstalled)
                return LaunchResult.Fail("Error_LoaderMissing");

            if (!fs.File.Exists(profile.PreloaderPath) && !await loader.EnsureProfileCoreAsync(profile.RootPath, ct).ConfigureAwait(false))
                return LaunchResult.Fail("Error_LoaderMissing");
        }

        var mode = settings.Current.LaunchMode;
        LaunchCommand command;
        try
        {
            command = LaunchCommandBuilder.Build(game, mode, profile?.PreloaderPath, env.HostPlatform, SteamExecutable(), MacArchitecturesOf(game, mode, profile));
        }
        catch (SilkflyException ex)
        {
            logger.LogWarning("Cannot launch: {Error}", ex.MessageKey);
            return LaunchResult.Fail(ex.MessageKey, [.. ex.Args]);
        }

        if (command.ConfigTarget is { } config)
            await loader.SetDoorstopConfigAsync(game, config.Enabled, config.TargetAssembly, ct).ConfigureAwait(false);

        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = command.WorkingDirectory ?? string.Empty,
        };
        foreach (var arg in command.Arguments)
            info.ArgumentList.Add(arg);
        foreach (var (name, value) in command.Environment ?? new Dictionary<string, string>())
            info.Environment[name] = value;

        Process? process;
        try
        {
            logger.LogInformation("Launching {Mode} ({Kind}): {File} {Args} {Environment}",
                mode, profile is null ? "vanilla" : $"profile {profile.Name}", info.FileName, string.Join(' ', command.Arguments),
                string.Join(' ', (command.Environment ?? new Dictionary<string, string>()).Select(e => $"{e.Key}={e.Value}")));
            // Process.Start can block (error dialogs), keep it off the UI thread
            process = await Task.Run(() => processes.Start(info), ct).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogError(ex, "Could not start {File}", info.FileName);
            return LaunchResult.Fail("Error_LaunchFailed", ex.Message);
        }

        // read the id before the watcher disposes the process
        int? processId = null;
        try
        {
            processId = process?.Id;
        }
        catch (InvalidOperationException)
        {
            // already exited
        }

        if (profile is not null)
            await profiles.MarkPlayedAsync(profile, ct).ConfigureAwait(false);

        SetRunning(true, profile);
        _watch?.Cancel();
        _watch = new CancellationTokenSource();
        _ = command.TracksGameProcess && process is not null
            ? WatchProcessAsync(process, _watch.Token)
            : WatchByNameAsync(_watch.Token);

        return new LaunchResult(true, processId, null);
    }

    // a game started before Silkfly was (re)opened
    public void AttachToRunningGame()
    {
        if (IsGameRunning || processes.FindByName(GameInfo.ProcessName).FirstOrDefault() is not { } game)
            return;

        var profile = ProfileStartedWith(game);
        logger.LogInformation("The game is already running (process {Id}), profile {Profile}", game.Id, profile?.Name ?? "unknown");
        SetRunning(true, profile);
        _watch?.Cancel();
        _watch = new CancellationTokenSource();
        _ = WatchProcessAsync(game, _watch.Token);
    }

    // the profile marked as played right before the game process started (through Steam the game starts later)
    private Profile? ProfileStartedWith(Process game)
    {
        DateTimeOffset started;
        try
        {
            started = game.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }

        return profiles.Profiles
            .Where(p => p.LastPlayedAt is { } played && played - started < TimeSpan.FromSeconds(30) && started - played < TimeSpan.FromMinutes(3))
            .MaxBy(p => p.LastPlayedAt);
    }

    private async Task WatchProcessAsync(Process process, CancellationToken ct)
    {
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Game exited with code {Code}", process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            process.Dispose();
        }

        SetRunning(false, null);
    }

    private async Task WatchByNameAsync(CancellationToken ct)
    {
        try
        {
            var deadline = DateTime.UtcNow + SteamAppearTimeout;
            Process? game = null;
            while (game is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                game = processes.FindByName(GameInfo.ProcessName).FirstOrDefault();
            }

            if (game is not null)
            {
                using (game)
                    await game.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                logger.LogWarning("The game process did not appear within {Timeout}", SteamAppearTimeout);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SetRunning(false, null);
    }

    private void SetRunning(bool running, Profile? profile)
    {
        IsGameRunning = running;
        RunningProfile = profile;
        RunningChanged?.Invoke(this, EventArgs.Empty);
    }

    private MacArchitectures? MacArchitecturesOf(GameInstall game, LaunchMode mode, Profile? profile)
    {
        if (profile is null || mode != LaunchMode.Direct || env.HostPlatform != GamePlatform.MacOS || game.Platform != GamePlatform.MacOS)
            return null;

        var mac = new MacArchitectures(
            env.HostArchitecture == System.Runtime.InteropServices.Architecture.Arm64,
            MachO.Read(fs, LaunchCommandBuilder.MacDoorstopPath(game)),
            MachO.Read(fs, LaunchCommandBuilder.MacGameBinary(game)));
        logger.LogInformation("macOS architectures: host arm64 {Arm64}, doorstop {Doorstop}, game {Game}", mac.HostIsArm64, mac.Doorstop, mac.Game);
        return mac;
    }

    private string? SteamExecutable() =>
        env.HostPlatform == GamePlatform.Windows
            ? env.GetRegistryValue(RegistryRoot.CurrentUser, @"Software\Valve\Steam", "SteamExe")?.Replace('/', '\\')
            : "steam";
}
