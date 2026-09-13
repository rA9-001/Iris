using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Iris.Localization;

public enum AppLanguage
{
    English,
    German,
}

/// <summary>
/// The language Iris is showing, and the text that goes with it.
///
/// Switching is live: XAML pulls its text through <see cref="StrExtension"/>, which binds
/// to the indexer here, so raising one property-changed for the indexer re-reads every
/// label in every open window at once. Text built in code is re-applied by whoever owns
/// it, from <see cref="Changed"/>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>The binding source. One instance, because the language is global.</summary>
    public static Loc Current { get; } = new();

    private static IReadOnlyDictionary<TextKey, string> _table = English.Table;

    private Loc()
    {
    }

    public static AppLanguage Language { get; private set; } = AppLanguage.English;

    /// <summary>Raised after the language changes, for text that is not XAML-bound.</summary>
    public static event Action? Changed;

    public static void Use(AppLanguage language)
    {
        if (language == Language) return;

        Language = language;
        _table = language == AppLanguage.German ? German.Table : English.Table;

        // Empty name means "every property", which is how a binding to an indexer is
        // told to re-read.
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(null));
        Changed?.Invoke();
    }

    /// <summary>
    /// The text for a key. An untranslated key falls back to English rather than showing
    /// a blank, so a half-finished language is still usable.
    /// </summary>
    public static string Get(TextKey key) =>
        _table.TryGetValue(key, out var text) ? text
            : English.Table.TryGetValue(key, out var fallback) ? fallback
            : key.ToString();

    /// <summary>
    /// The culture numbers and dates are formatted with. Tied to the chosen language, not
    /// to the Windows region: a German window showing "10.5 s" with an English decimal
    /// point looks wrong regardless of what the OS is set to.
    /// </summary>
    public static CultureInfo Culture =>
        Language == AppLanguage.German
            ? CultureInfo.GetCultureInfo("de-DE")
            : CultureInfo.GetCultureInfo("en-GB");

    public static string Get(TextKey key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    /// <summary>Indexer for XAML bindings; the key arrives as its enum name.</summary>
    public string this[string key] =>
        Enum.TryParse<TextKey>(key, out var parsed) ? Get(parsed) : key;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// <c>Text="{loc:Str StartOpenVideo}"</c>. Produces a one-way binding rather than a plain
/// string, which is what lets the language change without rebuilding the window.
/// </summary>
public sealed class StrExtension : MarkupExtension
{
    public StrExtension()
    {
    }

    public StrExtension(TextKey key) => Key = key;

    public TextKey Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = Loc.Current,
            Mode = BindingMode.OneWay,
        }.ProvideValue(serviceProvider);
}
