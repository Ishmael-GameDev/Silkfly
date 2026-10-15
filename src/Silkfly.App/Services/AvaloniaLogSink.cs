using Avalonia.Logging;
using Serilog;

namespace Silkfly.App.Services;

public sealed class AvaloniaLogSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var logger = Serilog.Log.ForContext("SourceContext", $"Avalonia.{area}");
        var template = messageTemplate + " (source: {AvaloniaSource})";
        object?[] values = [.. propertyValues, source?.GetType().Name];
        switch (level)
        {
            case LogEventLevel.Fatal:
            case LogEventLevel.Error:
                logger.Error(template, values);
                break;
            default:
                logger.Warning(template, values);
                break;
        }
    }
}
