namespace Silkfly.Core.Infrastructure;

public enum PluralForm { One, Few, Many }

public static class Plural
{
    public static PluralForm Of(long n, string language)
    {
        n = Math.Abs(n);
        if (language != "ru")
            return n == 1 ? PluralForm.One : PluralForm.Many;

        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11)
            return PluralForm.One;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return PluralForm.Few;
        return PluralForm.Many;
    }
}
