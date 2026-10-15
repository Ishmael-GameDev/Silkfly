using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Localization;

public static class RelativeTime
{
    public static string Format(ILocalizer localizer, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1))
            return localizer["Time_JustNow"];

        var (value, unit) = elapsed switch
        {
            _ when elapsed < TimeSpan.FromHours(1) => ((long)elapsed.TotalMinutes, "Minutes"),
            _ when elapsed < TimeSpan.FromDays(1) => ((long)elapsed.TotalHours, "Hours"),
            _ => ((long)elapsed.TotalDays, "Days"),
        };

        var form = Plural.Of(value, localizer.Culture.TwoLetterISOLanguageName);
        return localizer.Format($"Time_{unit}_{form}", value);
    }
}
