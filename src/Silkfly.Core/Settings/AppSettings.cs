using Silkfly.Core.Updates;

namespace Silkfly.Core.Settings;

public enum LaunchMode { Direct, ViaSteam, ProtonConfig }

public enum DependencyPolicy { LatestSameMajor, Exact, Latest }

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

// plain setters: with init, the STJ source generator resets missing properties instead of keeping the defaults
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultThunderstoreNamespace = "Ishmael";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? GamePath { get; set; }
    public LaunchMode LaunchMode { get; set; } = LaunchMode.Direct;
    public string? ProfilesRoot { get; set; }
    public Guid? ActiveProfileId { get; set; }
    public string Language { get; set; } = "auto";
    public double UiScale { get; set; } = 1.0;
    public bool ShowDeprecated { get; set; }
    public bool ShowNsfw { get; set; }
    public int WhatsNewDays { get; set; } = 7;
    public DependencyPolicy DependencyPolicy { get; set; } = DependencyPolicy.LatestSameMajor;
    public bool CheckForUpdates { get; set; } = true;
    public string ThunderstoreNamespace { get; set; } = DefaultThunderstoreNamespace;

    public bool VerboseLogging { get; set; }
    public string UpdateChannel { get; set; } = UpdateChannels.Stable;

    public DateTimeOffset? LastUpdateCheck { get; set; }
    public WindowPlacement? Window { get; set; }

    public AppSettings Normalized() => this with
    {
        UiScale = UiScale is >= 0.9 and <= 1.3 ? UiScale : 1.0,
        WhatsNewDays = WhatsNewDays is >= 1 and <= 365 ? WhatsNewDays : 7,
        Language = string.IsNullOrWhiteSpace(Language) ? "auto" : Language,
        ThunderstoreNamespace = string.IsNullOrWhiteSpace(ThunderstoreNamespace) ? DefaultThunderstoreNamespace : ThunderstoreNamespace.Trim(),
        UpdateChannel = UpdateChannel is UpdateChannels.Beta ? UpdateChannels.Beta : UpdateChannels.Stable,
    };
}
