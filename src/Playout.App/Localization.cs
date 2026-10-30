using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// The interface language (Settings → Station, applied at the next start). The translations are tables of English
/// text → translation: those built in (i18n/*.json), and any xx.json put in a <see cref="Folders">translations
/// folder</see>, which adds a language or corrects one without a new version. Windows are translated as they are made
/// (<see cref="Apply"/>); texts set from code go through <see cref="L"/>.
/// </summary>
static class Localization
{
    /// <summary>
    /// Where translation files are looked for, besides the built-in ones: next to the program (for every user of the
    /// computer) and in the settings folder. A file named after a language (de.json, es.json, pt-BR.json) adds it;
    /// one named as a built-in language corrects or completes its texts.
    /// </summary>
    public static string[] Folders =>
        [Path.Combine(AppContext.BaseDirectory, "translations"), Path.Combine(AppSettings.DataFolder, "translations")];

    static (string Code, string Name)[]? _languages;

    /// <summary>"" (automatic), English, then every language with a translation, by name.</summary>
    public static (string Code, string Name)[] Languages => _languages ??= Find();

    /// <summary>Looks at the folders again (a file added since).</summary>
    internal static void Rescan() => _languages = null;

    static (string Code, string Name)[] Find()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            if (name.StartsWith("i18n.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)) codes.Add(name[5..^5]);
        foreach (var file in Files()) codes.Add(Path.GetFileNameWithoutExtension(file));
        codes.Remove("en");
        var found = codes.Where(IsLanguage).Select(c => (Code: c, Name: Name(c))).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);
        return [("", "Automatic (this computer's language)"), ("en", "English"), .. found];
    }

    /// <summary>The translation files in the folders (not the built-in ones).</summary>
    static IEnumerable<string> Files() =>
        Folders.Where(Directory.Exists).SelectMany(f =>
        {
            try { return Directory.GetFiles(f, "*.json"); }
            catch (Exception) { return []; }
        });

    static bool IsLanguage(string code)
    {
        try { return !string.IsNullOrEmpty(CultureInfo.GetCultureInfo(code).Name); }
        catch (CultureNotFoundException) { return false; }
    }

    /// <summary>The language's own name for itself ("Deutsch", "Español"), as in the list of languages.</summary>
    static string Name(string code)
    {
        string name = CultureInfo.GetCultureInfo(code).NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], CultureInfo.GetCultureInfo(code)) + name[1..] : code;
    }

    /// <summary>The language to use for a setting: "" follows the computer (one there is a translation for, else English).</summary>
    public static string Resolve(string? setting)
    {
        bool Has(string code) => Languages.Any(l => l.Code.Length > 0 && l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(setting) && Has(setting)) return Languages.First(l => l.Code.Equals(setting, StringComparison.OrdinalIgnoreCase)).Code;
        var system = CultureInfo.CurrentUICulture;
        return Languages.FirstOrDefault(l => l.Code.Length > 0 && l.Code.Equals(system.Name, StringComparison.OrdinalIgnoreCase)).Code
            ?? Languages.FirstOrDefault(l => l.Code.Length > 0 && l.Code.Equals(system.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)).Code
            ?? "en";
    }

    /// <summary>How to make a translation file: the README put in the translations folder.</summary>
    public const string TranslatorNotes = """
        Sintoniza translations
        ======================

        A file here named after a language adds it to Settings -> Station -> Language (from the next start):
        de.json for German, es.json for Spanish, pt-BR.json for Brazilian Portuguese, and so on.
        A file named after a language Sintoniza already has (pt.json, fr.json) corrects or completes its texts.

        The file is a list of the English texts and their translations:

            {
             "Add pause": "Pause hinzufügen",
             "{0} items · {1}": "{0} Einträge · {1}"
            }

        Keep {0}, {1:HH:mm:ss} and the like as they are: the program puts values there.
        A text left out (or left empty) stays in English.

        To start, take a built-in language's file (src/Playout.App/i18n/pt.json in the source code) or ask for a
        file with every text and an empty translation (scripts/i18n-check.py --template de in the source code).
        Send a finished file to the developers, and it can be built in for everyone.
        """;

    /// <summary>Problems reading the translation files (a file that isn't valid JSON), for the log; "" when none.</summary>
    public static string Problems { get; private set; } = "";

    /// <summary>Loads the translations; dates and numbers follow the language too, unless the computer already uses it.</summary>
    public static void Load(string? setting)
    {
        string language = Resolve(setting);
        var texts = new Dictionary<string, string>();
        var problems = new List<string>();
        if (language != "en")
        {
            // A country's variant (pt-BR) over its language (pt): what it doesn't have comes from there, not English.
            var tables = language.Contains('-') ? [language.Split('-')[0], language] : new[] { language };
            foreach (var table in tables)
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"i18n.{table}.json"))
                    if (stream != null)
                        foreach (var (english, text) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new())
                            texts[english] = text;
            }
            // Then the folders' files for it, over the built-in texts (the settings folder's last).
            foreach (var file in tables.SelectMany(table => Files().Where(f => Path.GetFileNameWithoutExtension(f).Equals(table, StringComparison.OrdinalIgnoreCase))))
            {
                try
                {
                    var more = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
                    foreach (var (english, text) in more) if (!string.IsNullOrEmpty(text)) texts[english] = text;
                }
                catch (Exception ex) { problems.Add($"{file}: {ex.Message}"); }
            }
            var culture = Specific(language);
            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName != culture.TwoLetterISOLanguageName)
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = culture;
        }
        var ui = Specific(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = ui;
        Problems = string.Join("; ", problems);
        L.Use(language, texts);
    }

    /// <summary>The culture for dates and numbers in a language: Portugal's Portuguese, France's French, British English.</summary>
    static CultureInfo Specific(string language)
    {
        try
        {
            return language switch
            {
                "pt" => CultureInfo.GetCultureInfo("pt-PT"),
                "fr" => CultureInfo.GetCultureInfo("fr-FR"),
                "en" => CultureInfo.GetCultureInfo("en-GB"),
                _ => CultureInfo.CreateSpecificCulture(language),
            };
        }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-GB"); }
    }

    /// <summary>
    /// Translates the texts a window or control was made with (labels, buttons, tabs, tips, placeholders, its title).
    /// Call right after InitializeComponent, before any data is shown, so song titles and the like are never touched.
    /// </summary>
    public static void Apply(StyledElement root)
    {
        if (L.Language == "en") return;
        foreach (var element in root.GetSelfAndLogicalDescendants().OfType<StyledElement>())
        {
            switch (element)
            {
                case TextBlock t when t.Text is { } text && L.TryTranslate(text, out var tr): t.Text = tr; break;
                case HeaderedContentControl h when h.Header is string header && L.TryTranslate(header, out var tr): h.Header = tr; break;
                case MenuItem m when m.Header is string header && L.TryTranslate(header, out var tr): m.Header = tr; break;
                case ContentControl c when c.Content is string content && L.TryTranslate(content, out var tr): c.Content = tr; break;
                case TextBox box when box.PlaceholderText is { } placeholder && L.TryTranslate(placeholder, out var tr): box.PlaceholderText = tr; break;
                case CalendarDatePicker date when date.PlaceholderText is { } placeholder && L.TryTranslate(placeholder, out var tr): date.PlaceholderText = tr; break;
                case AutoCompleteBox auto when auto.PlaceholderText is { } placeholder && L.TryTranslate(placeholder, out var tr): auto.PlaceholderText = tr; break;
                case ComboBox combo when combo.PlaceholderText is { } placeholder && L.TryTranslate(placeholder, out var tr): combo.PlaceholderText = tr; break;
            }
            if (element is Window w && w.Title is { } title && L.TryTranslate(title, out var t2)) w.Title = t2;
            if (element is Control control && ToolTip.GetTip(control) is string tip && L.TryTranslate(tip, out var t3)) ToolTip.SetTip(control, t3);
        }
    }
}
