using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Silkfly.App.Localization;

public sealed class StrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => CreateBinding(Key);

    // indexer bindings don't refresh on language change
    public static IBinding CreateBinding(string key) => Localizer.Instance.Observe(key).ToBinding();
}
