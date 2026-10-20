namespace Playout.App;

/// <summary>
/// The user manual (PDF), installed next to the program: Sintoniza-Manual-PT.pdf in Portuguese, Sintoniza-Manual.pdf in
/// English (and for the languages without their own). On Linux also in /usr/share/doc/radio-playout.
/// </summary>
static class Manual
{
    public static string? Find()
    {
        string language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; // as Localization.Load set it
        string[] names = language == "en" ? ["Sintoniza-Manual.pdf"] : [$"Sintoniza-Manual-{language.ToUpperInvariant()}.pdf", "Sintoniza-Manual.pdf"];
        string[] folders = [AppContext.BaseDirectory, "/usr/share/doc/radio-playout"];
        return names.SelectMany(n => folders.Select(f => Path.Combine(f, n))).FirstOrDefault(File.Exists);
    }
}
