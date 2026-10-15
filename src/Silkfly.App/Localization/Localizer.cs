using System.Globalization;
using System.Resources;
using Silkfly.Core.Infrastructure;

namespace Silkfly.App.Localization;

public sealed class Localizer : ILocalizer
{
    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "ru"];

    private readonly ResourceManager _resources =
        new("Silkfly.App.Localization.Strings", typeof(Localizer).Assembly);

    private Localizer()
    {
    }

    public static Localizer Instance { get; } = new();

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public string this[string key] => _resources.GetString(key, Culture) ?? key;

    public event EventHandler? CultureChanged;

    public string Format(string key, params object[] args) => string.Format(Culture, this[key], args);

    public IObservable<string> Observe(string key) => new KeyObservable(this, key);

    public void SetCulture(string code)
    {
        var resolved = ResolveLanguage(code);
        var culture = CultureInfo.GetCultureInfo(resolved);

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        if (Equals(Culture, culture))
            return;

        Culture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string ResolveLanguage(string code)
    {
        if (SupportedLanguages.Contains(code))
            return code;

        var system = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Contains(system) ? system : "en";
    }

    private sealed class KeyObservable(Localizer localizer, string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            void OnCultureChanged(object? sender, EventArgs e) => observer.OnNext(localizer[key]);

            localizer.CultureChanged += OnCultureChanged;
            observer.OnNext(localizer[key]);
            return new Unsubscriber(() => localizer.CultureChanged -= OnCultureChanged);
        }
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
