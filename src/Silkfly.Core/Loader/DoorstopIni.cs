using System.Text;

namespace Silkfly.Core.Loader;

public static class DoorstopIni
{
    public static string Set(string text, string section, IReadOnlyDictionary<string, string> values)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var pending = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        var inSection = false;
        var sectionEnd = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inSection && sectionEnd < 0)
                    sectionEnd = i;
                inSection = string.Equals(trimmed[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = trimmed[..eq].Trim();
            if (pending.Remove(key, out var value))
                lines[i] = $"{key} = {value}";
        }

        if (pending.Count > 0)
        {
            var insertAt = sectionEnd >= 0 ? sectionEnd : lines.Count;
            if (!lines.Any(l => string.Equals(l.Trim(), $"[{section}]", StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add($"[{section}]");
                insertAt = lines.Count;
            }

            lines.InsertRange(insertAt, pending.Select(kv => $"{kv.Key} = {kv.Value}"));
        }

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Count - 1)
                sb.Append(newline);
        }

        return sb.ToString();
    }

    public static string? Get(string text, string section, string key)
    {
        var inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=');
            if (inSection && eq > 0 && string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line[(eq + 1)..].Trim();
        }

        return null;
    }
}
