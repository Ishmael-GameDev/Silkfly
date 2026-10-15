using System.Text;

namespace Silkfly.Core.Game;

public sealed class VdfNode
{
    public string? Value { get; init; }

    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;
}

public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseChildren(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseChildren(string text, ref int pos, VdfNode parent, bool topLevel)
    {
        while (true)
        {
            var key = ReadToken(text, ref pos);
            if (key is null)
            {
                if (!topLevel)
                    throw new FormatException("Unexpected end of VDF: missing '}'");
                return;
            }

            if (key == "}")
            {
                if (topLevel)
                    throw new FormatException("Unexpected '}' in VDF");
                return;
            }

            var next = ReadToken(text, ref pos) ?? throw new FormatException($"Missing value for '{key}'");
            if (next == "{")
            {
                var child = new VdfNode();
                ParseChildren(text, ref pos, child, topLevel: false);
                parent.Children[key] = child;
            }
            else
            {
                parent.Children[key] = new VdfNode { Value = next };
            }
        }
    }

    private static string? ReadToken(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n')
                    pos++;
            }
            else
            {
                break;
            }
        }

        if (pos >= text.Length)
            return null;

        var ch = text[pos];
        if (ch is '{' or '}')
        {
            pos++;
            return ch.ToString();
        }

        var sb = new StringBuilder();
        if (ch == '"')
        {
            pos++;
            while (pos < text.Length && text[pos] != '"')
            {
                if (text[pos] == '\\' && pos + 1 < text.Length)
                {
                    pos++;
                    sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', _ => text[pos] });
                }
                else
                {
                    sb.Append(text[pos]);
                }

                pos++;
            }

            pos++;
            return sb.ToString();
        }

        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"'))
            sb.Append(text[pos++]);
        return sb.ToString();
    }
}
