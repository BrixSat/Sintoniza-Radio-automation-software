using System.Globalization;

namespace Playout.Engine;

/// <summary>
/// The interface language. Texts are written in English and looked up in the chosen language's table (English
/// text → translation); a text that has no translation stays in English. Set once at start (<see cref="Use"/>).
/// </summary>
public static class L
{
    static IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>();

    /// <summary>"en", "pt", "fr", or another language with a translation file ("de", "pt-BR").</summary>
    public static string Language { get; private set; } = "en";

    public static void Use(string language, IReadOnlyDictionary<string, string> texts)
    {
        Language = language;
        _texts = texts;
    }

    /// <summary>A text in the interface language.</summary>
    public static string T(string english) => _texts.TryGetValue(english, out var text) && text.Length > 0 ? text : english;

    /// <summary>A text with values ({0}, {1}…) in the interface language.</summary>
    public static string F(string english, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>Whether a text has a translation (used to translate the windows' own texts).</summary>
    public static bool TryTranslate(string english, out string text)
    {
        if (_texts.TryGetValue(english, out var t) && t.Length > 0)
        {
            text = t;
            return true;
        }
        text = english;
        return false;
    }
}
