using Silkfly.Core.Infrastructure;

namespace Silkfly.Core.Tests.Infrastructure;

public sealed class PluralTests
{
    [Theory]
    [InlineData(1, PluralForm.One)]
    [InlineData(21, PluralForm.One)]
    [InlineData(2, PluralForm.Few)]
    [InlineData(24, PluralForm.Few)]
    [InlineData(5, PluralForm.Many)]
    [InlineData(11, PluralForm.Many)]
    [InlineData(12, PluralForm.Many)]
    [InlineData(14, PluralForm.Many)]
    [InlineData(111, PluralForm.Many)]
    [InlineData(0, PluralForm.Many)]
    public void Russian(long n, PluralForm expected) => Plural.Of(n, "ru").ShouldBe(expected);

    [Theory]
    [InlineData(1, PluralForm.One)]
    [InlineData(2, PluralForm.Many)]
    [InlineData(21, PluralForm.Many)]
    public void English(long n, PluralForm expected) => Plural.Of(n, "en").ShouldBe(expected);
}
