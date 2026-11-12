using Avalonia.Controls;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Library → Music report: the most played songs and artists of a period, each category's share, how
/// many hours kept the templates' targets, and the songs of a category not played for a while (<see cref="MusicReport"/>).
/// </summary>
public partial class MusicReportView : UserControl
{
    static readonly int[] Periods = [7, 30, 90];

    readonly LibraryDb? _db;
    readonly Func<ScheduleData>? _data;
    List<LibraryCategory> _categories = new();
    int _loadVersion, _lookVersion, _playsVersion;
    bool _shown;

    // Parameterless constructor for the XAML previewer.
    public MusicReportView() => InitializeComponent();

    /// <param name="data">The schedule as edited (its templates' targets).</param>
    public MusicReportView(LibraryDb db, Func<ScheduleData> data)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _data = data;
        PeriodBox.ItemsSource = Periods.Select(d => L.F("The last {0} days", d)).ToList();
        PeriodBox.SelectedIndex = 1;
        PeriodBox.SelectionChanged += async (_, _) => await Reload();
        ReloadButton.Click += async (_, _) => await Reload();
        NotPlayedButton.Click += async (_, _) => await LookNotPlayed();
        PlaysToBox.SelectedDate = StationTime.Today;
        PlaysFromBox.SelectedDate = StationTime.Today.AddDays(-29);
        SongPlaysButton.Click += async (_, _) => await LookSongPlays();
        SongSearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            e.Handled = true;
            await LookSongPlays();
        };
        // The first time it is shown; then on Refresh or another period (not each time the tab is opened again).
        AttachedToVisualTree += async (_, _) => { if (!_shown) { _shown = true; await Reload(); } };
    }

    internal async Task Reload()
    {
        if (_db is not { } db || _data is not { } data) return;
        int version = ++_loadVersion;
        int days = Periods[Math.Max(0, PeriodBox.SelectedIndex)];
        var to = StationTime.Today;
        var from = to.AddDays(-(days - 1));
        var schedule = data().Clone();
        StatusText.Text = L.T("Reading…");
        try
        {
            var (report, categories) = await Task.Run(() => (MusicReport.Make(db, schedule, from, to), db.Categories().ToList()));
            if (version != _loadVersion) return;
            SongsList.ItemsSource = report.Songs;
            ArtistsList.ItemsSource = report.Artists;
            CategoriesList.ItemsSource = report.Categories;
            TargetsList.ItemsSource = report.Targets;
            NoTargetsText.IsVisible = report.Targets.Count == 0;
            StatusText.Text = L.F("{0:dd/MM/yyyy} to {1:dd/MM/yyyy}: {2} songs played", from, to, report.Plays);
            if (_categories.Count == 0)
            {
                var tree = CategoryTree.Flatten(categories);
                _categories = tree.Select(x => x.Category).ToList();
                CategoryBox.ItemsSource = tree.Select(x => new string(' ', 4 * x.Depth) + x.Category.Name).ToList();
                CategoryBox.SelectedIndex = _categories.FindIndex(c => c.Kind == "music" && c.ParentId == null);
            }
        }
        catch (Exception ex)
        {
            if (version == _loadVersion) StatusText.Text = L.F("Can't make the report: {0}", ex.Message);
        }
    }

    /// <summary>When the songs matching the search went on air in the days chosen.</summary>
    async Task LookSongPlays()
    {
        if (_db is not { } db) return;
        string search = SongSearchBox.Text?.Trim() ?? "";
        if (search.Length == 0)
        {
            SongPlaysText.Text = L.T("Type an artist or a title first.");
            return;
        }
        var from = (PlaysFromBox.SelectedDate ?? StationTime.Today.AddDays(-29)).Date;
        var to = (PlaysToBox.SelectedDate ?? StationTime.Today).Date;
        if (to < from) (from, to) = (to, from);
        int version = ++_playsVersion;
        SongPlaysText.Text = L.T("Reading…");
        try
        {
            var (songs, plays, lines) = await Task.Run(() => MusicReport.SongPlays(db, search, from, to));
            if (version != _playsVersion) return;
            SongPlaysList.ItemsSource = lines;
            SongPlaysText.Text = songs == 0 ? L.F("Nothing matching “{0}” went on air from {1:dd/MM/yyyy} to {2:dd/MM/yyyy}.", search, from, to)
                : L.F("{0:dd/MM/yyyy} to {1:dd/MM/yyyy}: {2} plays of {3} songs, the most played first.", from, to, plays, songs)
                  + (lines.Count < songs + plays ? " " + L.F("Only the first {0} lines are shown: search with more words or fewer days.", lines.Count) : "");
        }
        catch (Exception ex) { if (version == _playsVersion) SongPlaysText.Text = L.F("Can't read them: {0}", ex.Message); }
    }

    async Task LookNotPlayed()
    {
        if (_db is not { } db) return;
        if (CategoryBox.SelectedIndex is not (>= 0 and var i) || i >= _categories.Count)
        {
            NotPlayedText.Text = L.T("Choose a category first.");
            return;
        }
        long category = _categories[i].Id;
        int days = (int)(DaysBox.Value ?? 30);
        int version = ++_lookVersion;
        NotPlayedText.Text = L.T("Reading…");
        try
        {
            var (count, lines) = await Task.Run(() => MusicReport.NotPlayed(db, category, days, StationTime.Now));
            if (version != _lookVersion) return;
            NotPlayedList.ItemsSource = lines;
            NotPlayedText.Text = count == 0 ? L.T("Every song of it has played in that time.")
                : count > lines.Count ? L.F("{0} songs; the {1} rested longest:", count, lines.Count) : L.F("{0} songs:", count);
        }
        catch (Exception ex) { NotPlayedText.Text = L.F("Can't read them: {0}", ex.Message); }
    }
}
