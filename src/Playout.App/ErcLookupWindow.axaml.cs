using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → ERC quotas → Look up on MusicBrainz: each song's performer country, language and first edition from
/// MusicBrainz, and what that suggests for the ERC; what is ticked is saved. Closes with true when something was saved.
/// </summary>
public partial class ErcLookupWindow : Window
{
    /// <summary>A song, what was found and what is suggested.</summary>
    sealed class Row(LibraryTrack track)
    {
        public LibraryTrack Track { get; } = track;
        public (bool? Portuguese, string? Language, string? Nationality, string? Release) Suggested;
        public CheckBox Tick { get; } = new() { IsEnabled = false, VerticalAlignment = VerticalAlignment.Top, Margin = new Avalonia.Thickness(0, 2, 6, 0) };
        public TextBlock Found { get; } = new() { FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Text = L.T("waiting…") };
        public TextBlock Suggestion { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        public bool HasSuggestion => Suggested.Portuguese != null || Suggested.Language != null || Suggested.Nationality != null || Suggested.Release != null;
    }

    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8B93A3")), Yes = new SolidColorBrush(Color.Parse("#3ECF8E"));

    /// <summary>Country names in Portuguese, for a station that wrote its list in Portuguese.</summary>
    static readonly Dictionary<string, string[]> PortugueseNames = new()
    {
        ["PT"] = ["Portugal"], ["BR"] = ["Brasil"], ["ES"] = ["Espanha"], ["FR"] = ["França"], ["GB"] = ["Reino Unido", "Inglaterra"],
        ["US"] = ["Estados Unidos", "EUA"], ["IT"] = ["Itália"], ["DE"] = ["Alemanha"], ["AO"] = ["Angola"], ["MZ"] = ["Moçambique"],
        ["CV"] = ["Cabo Verde"], ["IE"] = ["Irlanda"], ["NL"] = ["Países Baixos", "Holanda"], ["BE"] = ["Bélgica"], ["SE"] = ["Suécia"],
        ["GR"] = ["Grécia"], ["PL"] = ["Polónia"], ["AT"] = ["Áustria"], ["DK"] = ["Dinamarca"], ["FI"] = ["Finlândia"], ["CA"] = ["Canadá"],
    };

    readonly LibraryDb _db = null!;
    readonly IReadOnlyList<ErcCountry> _countries = [];
    readonly List<Row> _rows = new();
    readonly CancellationTokenSource _stop = new();
    bool _running;

    // Parameterless constructor for the XAML previewer.
    public ErcLookupWindow() => InitializeComponent();

    public ErcLookupWindow(LibraryDb db, IReadOnlyCollection<long> trackIds, IReadOnlyList<ErcCountry> countries)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _countries = countries;
        foreach (var track in db.GetByIds(trackIds.ToList())) _rows.Add(new Row(track));
        HeadText.Text = L.F("{0} songs", _rows.Count);
        Progress.Maximum = Math.Max(1, _rows.Count);
        foreach (var row in _rows) RowsPanel.Children.Add(View(row));

        StopButton.Click += (_, _) => _stop.Cancel();
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += async (_, _) => await Save();
        Opened += async (_, _) => await LookUp();
        Closed += (_, _) => _stop.Cancel();
    }

    static Control View(Row row)
    {
        string name = string.IsNullOrWhiteSpace(row.Track.Artist) ? row.Track.Title : $"{row.Track.Artist} - {row.Track.Title}";
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(row.Found);
        text.Children.Add(row.Suggestion);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(row.Tick);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border { Child = grid, Padding = new Avalonia.Thickness(10, 8), Background = new SolidColorBrush(Color.Parse("#181B22")) };
    }

    async Task LookUp()
    {
        _running = true;
        var mb = new MusicBrainz($"Sintoniza/{Updates.CurrentText} ( {Updates.ReleasesPage} )");
        int done = 0, found = 0;
        try
        {
            foreach (var row in _rows)
            {
                _stop.Token.ThrowIfCancellationRequested();
                row.Found.Text = L.T("looking…");
                try
                {
                    var info = await Task.Run(() => mb.LookUp(row.Track.Artist, row.Track.Title, row.Track.Isrc, _stop.Token));
                    Show(row, info);
                    if (info.Found) found++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    row.Found.Text = L.F("MusicBrainz didn't answer: {0}", ex.Message);
                }
                done++;
                Progress.Value = done;
                ProgressText.Text = L.F("{0} of {1} · {2} found", done, _rows.Count, found);
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var row in _rows.Where(r => !r.Tick.IsEnabled)) row.Found.Text = L.T("not looked up (stopped)");
            ProgressText.Text = L.F("Stopped: {0} of {1} · {2} found", done, _rows.Count, found);
        }
        _running = false;
        StopButton.IsEnabled = false;
        SaveButton.IsEnabled = _rows.Any(r => r.Tick.IsChecked == true);
        foreach (var row in _rows) row.Tick.IsCheckedChanged += (_, _) => SaveButton.IsEnabled = _rows.Any(r => r.Tick.IsChecked == true);
    }

    void Show(Row row, MusicBrainzInfo info)
    {
        if (!info.Found)
        {
            row.Found.Text = L.T("not found on MusicBrainz");
            return;
        }
        var facts = new List<string> { $"{info.Artist} - {info.Title}" };
        if (info.Country != null) facts.Add(CountryName(info.Country));
        if (info.Language != null) facts.Add(info.Language);
        if (info.FirstRelease != null) facts.Add(info.FirstRelease);
        row.Found.Text = L.F("MusicBrainz: {0}", string.Join(" · ", facts));

        row.Suggested = Erc.Suggest(info, ErcCode);
        var (portuguese, language, nationality, release) = row.Suggested;
        var parts = new List<string>
        {
            portuguese switch { true => L.T("Portuguese music"), false => L.T("not Portuguese music"), _ => L.T("Portuguese music or not: unknown") },
        };
        if (language != null) parts.Add(L.F("language {0}", language));
        if (nationality != null) parts.Add(L.F("country {0}", nationality));
        else if (info.Country != null) parts.Add(L.F("{0} isn't in the station's ERC countries", CountryName(info.Country)));
        if (release != null) parts.Add(L.F("first edition {0}", release));
        row.Suggestion.Text = "→ " + string.Join(" · ", parts);
        row.Suggestion.Foreground = portuguese == true ? Yes : Brushes.White;
        row.Tick.IsEnabled = row.HasSuggestion;
        row.Tick.IsChecked = row.HasSuggestion;
    }

    static string CountryName(string iso)
    {
        try { return $"{new RegionInfo(iso).EnglishName} ({iso})"; }
        catch { return iso; }
    }

    /// <summary>The station's ERC code for a country (ISO 3166), matched by the name or code in its list; null: not there.</summary>
    string? ErcCode(string iso)
    {
        var names = new HashSet<string> { Plain(iso) };
        try
        {
            var region = new RegionInfo(iso);
            foreach (var n in new[] { region.EnglishName, region.NativeName, region.DisplayName, region.ThreeLetterISORegionName }) names.Add(Plain(n));
        }
        catch { /* not a country .NET knows */ }
        foreach (var n in PortugueseNames.GetValueOrDefault(iso.ToUpperInvariant()) ?? []) names.Add(Plain(n));
        return _countries.FirstOrDefault(c => c.Code.Trim().Length > 0 && names.Contains(Plain(c.Name)))?.Code.Trim();
    }

    static string Plain(string s)
    {
        var text = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
        return text.ToString();
    }

    async Task Save()
    {
        if (_running) return;
        var ticked = _rows.Where(r => r.Tick.IsChecked == true && r.HasSuggestion).ToList();
        SaveButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                // Only what was found is set: what MusicBrainz doesn't know stays as it was.
                foreach (var row in ticked)
                {
                    var (portuguese, language, nationality, release) = row.Suggested;
                    _db.SetAttributes([row.Track.Id], new TrackAttributes(ErcPortuguese: portuguese, Language: language,
                        ErcNationality: nationality, ErcRelease: release));
                }
            });
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            SaveButton.IsEnabled = true;
        }
    }
}
