using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Silkfly.Core.Tests.Localization;

public sealed partial class LocalizationCompletenessTests
{
    private static readonly string LocalizationFolder =
        Path.Combine(FindRepositoryRoot(), "src", "Silkfly.App", "Localization");

    [Fact]
    public void Every_english_key_exists_in_russian()
    {
        var en = ReadResx("Strings.resx");
        var ru = ReadResx("Strings.ru.resx");

        en.Keys.Except(ru.Keys).ShouldBeEmpty();
        ru.Keys.Except(en.Keys).ShouldBeEmpty();
    }

    [Fact]
    public void No_value_is_empty()
    {
        foreach (var file in new[] { "Strings.resx", "Strings.ru.resx" })
        {
            var empty = ReadResx(file).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key);
            empty.ShouldBeEmpty(file);
        }
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        var en = ReadResx("Strings.resx");
        var ru = ReadResx("Strings.ru.resx");

        foreach (var (key, value) in en)
        {
            Placeholders(ru[key]).ShouldBe(Placeholders(value), key);
        }
    }

    private static Dictionary<string, string> ReadResx(string fileName) =>
        XDocument.Load(Path.Combine(LocalizationFolder, fileName))
            .Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static string[] Placeholders(string value) =>
        PlaceholderRegex().Matches(value).Select(m => m.Value).Distinct().Order().ToArray();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Silkfly.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Silkfly.sln not found above the test output folder");
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
