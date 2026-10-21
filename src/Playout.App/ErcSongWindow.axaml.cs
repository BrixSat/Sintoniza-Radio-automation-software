using Avalonia.Controls;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// What the ERC needs about one or more songs: Portuguese music or not, the language, the performer's country, the
/// first edition. Each can be the song's own or as its category says; with several songs, what differs between them
/// can be left as it is. Closes with true when saved.
/// </summary>
public partial class ErcSongWindow : Window
{
    /// <summary>A choice in a list: what it shows, and what it sets (<see cref="Keep"/>: nothing).</summary>
    sealed record Choice(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    static readonly object Keep = new(), FromCategory = new();

    /// <summary>The languages offered (any other code a song has is offered too).</summary>
    internal static readonly (string Code, string Name)[] Languages =
    [
        ("pt-PT", "Portuguese (Portugal)"), ("pt-BR", "Portuguese (Brazil)"), ("pt", "Portuguese (other)"), ("en", "English"), ("es", "Spanish"),
        ("fr", "French"), ("it", "Italian"), ("de", "German"), ("zxx", "No lyrics (instrumental)"),
    ];

    readonly LibraryDb _db = null!;
    readonly List<long> _ids = new();
    bool _releaseTyped;
    string? _rejected;

    // Parameterless constructor for the XAML previewer.
    public ErcSongWindow() => InitializeComponent();

    public ErcSongWindow(LibraryDb db, IReadOnlyCollection<long> trackIds, IReadOnlyList<ErcCountry> countries)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _ids = trackIds.ToList();
        var tracks = db.GetByIds(_ids);
        SongsText.Text = tracks.Count == 1
            ? (string.IsNullOrWhiteSpace(tracks[0].Artist) ? tracks[0].Title : $"{tracks[0].Artist} - {tracks[0].Title}")
            : L.F("{0} songs", tracks.Count);

        // Each list: "leave as they are" when the songs differ, "as its category says", then the values.
        bool Same<T>(Func<LibraryTrack, T> get) => tracks.Select(get).Distinct().Count() <= 1;
        Choice? keep = tracks.Count > 1 ? new Choice(L.T("(leave as they are)"), Keep) : null;
        var category = new Choice(L.T("As its category says"), FromCategory);

        var yes = new Choice(L.T("Yes: Portuguese music"), true);
        var no = new Choice(L.T("No"), false);
        PortugueseBox.ItemsSource = new[] { keep, category, yes, no }.OfType<Choice>().ToList();
        PortugueseBox.SelectedItem = !Same(t => t.ErcPortuguese) ? keep : tracks.FirstOrDefault()?.ErcPortuguese switch { true => yes, false => no, _ => category };

        var languages = Languages.Select(l => new Choice($"{L.T(l.Name)} · {l.Code}", l.Code)).ToList();
        foreach (var other in tracks.Select(t => t.Language).OfType<string>().Where(c => c.Length > 0).Distinct())
            if (languages.All(l => !string.Equals((string)l.Value!, other, StringComparison.OrdinalIgnoreCase))) languages.Add(new Choice(other, other));
        LanguageBox.ItemsSource = new[] { keep, category }.OfType<Choice>().Concat(languages).ToList();
        string? language = tracks.FirstOrDefault()?.Language;
        LanguageBox.SelectedItem = !Same(t => t.Language?.ToLowerInvariant()) ? keep
            : string.IsNullOrWhiteSpace(language) ? category
            : languages.First(l => string.Equals((string)l.Value!, language, StringComparison.OrdinalIgnoreCase));

        var countryChoices = countries.Select(c => new Choice($"{c.Name} · {c.Code}", c.Code)).ToList();
        foreach (var other in tracks.Select(t => t.ErcNationality).OfType<string>().Where(c => c.Length > 0).Distinct())
            if (countryChoices.All(c => (string)c.Value! != other)) countryChoices.Add(new Choice(L.F("Code {0}", other), other));
        CountryBox.ItemsSource = new[] { keep, category }.OfType<Choice>().Concat(countryChoices).ToList();
        string? nationality = tracks.FirstOrDefault()?.ErcNationality;
        CountryBox.SelectedItem = !Same(t => t.ErcNationality) ? keep
            : string.IsNullOrWhiteSpace(nationality) ? category : countryChoices.First(c => (string)c.Value! == nationality);

        if (Same(t => t.ErcRelease)) ReleaseBox.Text = tracks.FirstOrDefault()?.ErcRelease ?? "";
        else ReleaseBox.PlaceholderText = L.T("(they differ: leave empty to keep them)");
        _releaseTyped = Same(t => t.ErcRelease);
        ReleaseBox.TextChanged += (_, _) =>
        {
            _releaseTyped = true;
            if (ReleaseBox.Text != _rejected) ErrorText.Text = ""; // typed again
        };

        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += async (_, _) => await Save();
    }

    async Task Save()
    {
        string release = ReleaseBox.Text?.Trim() ?? "";
        if (!Erc.IsReleaseText(release))
        {
            ErrorText.Text = L.T("Write the first edition as yyyy-mm-dd, yyyy-mm or yyyy.");
            _rejected = ReleaseBox.Text;
            return;
        }
        object? Value(ComboBox box) => (box.SelectedItem as Choice)?.Value;
        var portuguese = Value(PortugueseBox);
        var language = Value(LanguageBox);
        var country = Value(CountryBox);
        var attributes = new TrackAttributes(
            ErcPortuguese: portuguese as bool?, ClearErcPortuguese: portuguese == FromCategory,
            Language: language as string, ClearLanguage: language == FromCategory,
            ErcNationality: country as string, ClearErcNationality: country == FromCategory,
            ErcRelease: _releaseTyped && release.Length > 0 ? release : null, ClearErcRelease: _releaseTyped && release.Length == 0);
        SaveButton.IsEnabled = false;
        try
        {
            await Task.Run(() => _db.SetAttributes(_ids, attributes));
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            SaveButton.IsEnabled = true;
        }
    }
}
