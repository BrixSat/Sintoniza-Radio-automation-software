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
/// The interface language: English, Portuguese or French (Settings → Station, applied at the next start). The
/// translations are in i18n/*.json (English text → translation). Windows are translated as they are made
/// (<see cref="Apply"/>); texts set from code go through <see cref="L"/>.
/// </summary>
static class Localization
{
    public static readonly (string Code, string Name)[] Languages = [("", "Automatic (this computer's language)"), ("en", "English"), ("pt", "Português"), ("fr", "Français")];

    /// <summary>The language to use for a setting: "" follows the computer (Portuguese, French, else English).</summary>
    public static string Resolve(string? setting) =>
        setting is "en" or "pt" or "fr" ? setting
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is var system && system is "pt" or "fr" ? system : "en";

    /// <summary>Loads the translations; dates and numbers follow the language too, unless the computer already uses it.</summary>
    public static void Load(string? setting)
    {
        string language = Resolve(setting);
        var texts = new Dictionary<string, string>();
        if (language != "en")
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"i18n.{language}.json");
            if (stream != null)
                texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? texts;
            if (CultureInfo.CurrentCulture.TwoLetterISOLanguageName != language)
            {
                var culture = CultureInfo.GetCultureInfo(language == "pt" ? "pt-PT" : "fr-FR");
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = culture;
            }
        }
        var ui = CultureInfo.GetCultureInfo(language == "pt" ? "pt-PT" : language == "fr" ? "fr-FR" : "en-GB");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = ui;
        L.Use(language, texts);
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
            }
            if (element is Window w && w.Title is { } title && L.TryTranslate(title, out var t2)) w.Title = t2;
            if (element is Control control && ToolTip.GetTip(control) is string tip && L.TryTranslate(tip, out var t3)) ToolTip.SetTip(control, t3);
        }
    }
}
