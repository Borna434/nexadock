using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using NexaDock.Core.Localization;
using NexaDock.Core.Models;

namespace NexaDock.UI.Localization;

/// <summary>
/// Current UI language and culture. Bindings created by <see cref="LocExtension"/> go through the
/// indexer, so changing the language updates every open window instantly.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    /// <summary>The resolved language (never <see cref="AppLanguage.System"/>).</summary>
    public AppLanguage Language { get; private set; } = AppLanguage.English;

    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    public string this[string key] => Strings.Get(Language, key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (view models refresh cached text).</summary>
    public static event EventHandler? LanguageChanged;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object[] args) => string.Format(Instance.Culture, T(key), args);

    /// <summary>Applies a language setting; returns true when the effective language changed.</summary>
    public static bool Apply(AppLanguage setting)
    {
        var resolved = Strings.Resolve(setting, CultureInfo.InstalledUICulture);
        var culture = resolved == AppLanguage.Croatian
            ? CultureInfo.GetCultureInfo("hr-HR")
            : (CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "en" ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo("en-GB"));

        // Keep the user's own regional format when it matches the chosen language.
        if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
        {
            culture = CultureInfo.CurrentCulture;
        }

        var changed = resolved != Instance.Language || !Equals(culture, Instance.Culture);
        Instance.Language = resolved;
        Instance.Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (changed)
        {
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke(Instance, EventArgs.Empty);
        }

        return changed;
    }
}

/// <summary>XAML: <c>Text="{l:Loc settings.title}"</c> — a live binding to a localized string.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
