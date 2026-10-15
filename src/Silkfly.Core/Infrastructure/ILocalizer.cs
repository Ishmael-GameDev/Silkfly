using System.Globalization;

namespace Silkfly.Core.Infrastructure;

public interface ILocalizer
{
    string this[string key] { get; }

    CultureInfo Culture { get; }

    void SetCulture(string code);

    string Format(string key, params object[] args);

    event EventHandler? CultureChanged;
}
