using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → ERC quotas: the Portuguese music quotas of a period (usually last month) as the ERC counts them —
/// the whole month and Monday to Friday 7–20h — from what went on air and, to the end of the period, the daily
/// playlists still to play; the songs that still need classifying; and the file the ERC's portal takes.
/// </summary>
public partial class ErcView : UserControl
{
    readonly LibraryDb? _db;
    readonly string _who = "";
    List<ErcCountry> _countries = Erc.DefaultCountries();
    string _alias = "";
    List<(ErcPlay Play, ErcSong? Song)> _played = new();
    (DateTime From, DateTime To) _checked;
    bool _loaded;

    /// <summary>A song that needs classifying, as listed.</summary>
    sealed record MissingRow(long? TrackId, string Path, string Text)
    {
        public override string ToString() => Text;
    }

    // Parameterless constructor for the XAML previewer.
    public ErcView() => InitializeComponent();

    /// <param name="who">Who saves the station's details (user@computer).</param>
    public ErcView(LibraryDb db, string who)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _who = who;
        SetMonth(-1);
        LastMonthButton.Click += (_, _) => { SetMonth(-1); _ = Check(); };
        ThisMonthButton.Click += (_, _) => { SetMonth(0); _ = Check(); };
        CheckButton.Click += async (_, _) => await Check();
        ExportButton.Click += async (_, _) => await Export();
        MissingList.SelectionChanged += (_, _) => Selected();
        LookupButton.Click += async (_, _) => await LookUp();
        AddToLibraryButton.Click += async (_, _) => await AddToLibrary();
        SelectAllButton.Click += (_, _) => MissingList.SelectAll();
        ClassifyButton.Click += async (_, _) => await Classify();
        AddCountryButton.Click += (_, _) => { _countries.Add(new ErcCountry()); ShowCountries(); };
        SaveDetailsButton.Click += async (_, _) => await SaveDetails();
        AttachedToVisualTree += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            await LoadDetails();
            ShowCategories();
            await Check();
        };
    }

    /// <summary>A month: 0 this one, -1 the one before.</summary>
    void SetMonth(int offset)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(offset);
        FromBox.SelectedDate = first;
        ToBox.SelectedDate = offset == 0 ? DateTime.Today : first.AddMonths(1).AddDays(-1);
    }

    (DateTime From, DateTime To) Period()
    {
        var (from, to) = (FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today);
        return to < from ? (to.Date, from.Date) : (from.Date, to.Date);
    }

    // ───────────────────────── The quotas ─────────────────────────

    async Task Check()
    {
        if (_db is not { } db) return;
        var (from, to) = Period();
        CheckButton.IsEnabled = ExportButton.IsEnabled = false;
        StatusText.Text = L.T("Counting…");
        try
        {
            var now = DateTime.Now;
            var (played, planned) = await Task.Run(() => Load(db, from, to, now));
            _played = played;
            _checked = (from, to);
            bool Eu(string? code) => code != null && _countries.Any(c => c.Eu && c.Code.Trim() == code.Trim());
            var daytime = played.Where(p => Erc.InWeekdayHours(p.Play.Start)).ToList();
            Show(AllPanel, Erc.Figures(played, Eu));
            Show(DaytimePanel, Erc.Figures(daytime, Eu));
            PlannedCard.IsVisible = planned.Count > 0;
            if (planned.Count > 0)
            {
                var all = played.Concat(planned).ToList();
                Show(PlannedAllPanel, Erc.Figures(all, Eu));
                Show(PlannedDaytimePanel, Erc.Figures(all.Where(p => Erc.InWeekdayHours(p.Play.Start)).ToList(), Eu));
            }
            ShowMissing(played.Concat(planned));
            StatusText.Text = played.Count == 0
                ? L.T("No songs went on air in this period.")
                : L.F("{0} songs on air from {1:d} to {2:d}.", played.Count, from, to)
                  + (planned.Count > 0 ? " " + L.F("{0} more in the daily playlists to the end of the period.", planned.Count) : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Can't count: {0}", ex.Message);
        }
        finally
        {
            CheckButton.IsEnabled = ExportButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The songs that went on air in the period (the as-run log), and those the daily playlists still have to play
    /// in it, each with what the library says about it.
    /// </summary>
    internal static (List<(ErcPlay Play, ErcSong? Song)> Played, List<(ErcPlay Play, ErcSong? Song)> Planned) Load(LibraryDb db, DateTime from, DateTime to, DateTime now)
    {
        var played = AsRunDb.Read(db, from, to).Where(e => e.Type == "Music")
            .Select(e => new ErcPlay(e.Start, e.Seconds ?? 0, e.Artist, e.Title, e.File, e.Isrc)).ToList();
        var planned = new List<ErcPlay>();
        var end = to.Date.AddDays(1);
        if (end > now)
        {
            var start = new DateTime(Math.Max(from.Date.Ticks, now.Date.AddHours(now.Hour).Ticks));
            foreach (var (hour, items) in new PlanStore(db).Items(start, end).OrderBy(h => h.Key))
            {
                var at = hour;
                foreach (var item in items.Where(i => !i.Optional))
                {
                    if (item.Kind == TrackKind.Music && at > now && item.Path.Length > 0)
                        planned.Add(new ErcPlay(at, item.Seconds, item.Artist, item.Title, item.Path, ""));
                    at = at.AddSeconds(Math.Max(0, item.Seconds));
                }
            }
        }
        var songs = db.ErcSongs(played.Concat(planned).Select(p => p.Path));
        (ErcPlay Play, ErcSong? Song) With(ErcPlay p)
        {
            var song = songs.GetValueOrDefault(p.Path);
            // The end wasn't logged (the program stopped while it played): the file's length.
            if (p.Seconds <= 0 && song != null) p = p with { Seconds = song.Duration };
            return (p, song);
        }
        return (played.Select(With).ToList(), planned.Select(With).ToList());
    }

    void Show(StackPanel panel, ErcFigures f)
    {
        panel.Children.Clear();
        panel.Children.Add(QuotaRow(L.T("Portuguese music"), L.F("of all {0} songs · at least {1:0} %", f.Songs, Erc.PortugueseMinimum), f.Portuguese));
        // These two are shares of the Portuguese music only (art. 43.º, 44.º): 2 of 2 Portuguese songs is 100 %, yet says
        // nothing while the Portuguese music itself is short, so then they aren't shown as met.
        bool base_ = f.Portuguese.Met;
        panel.Children.Add(QuotaRow(L.T("In Portuguese, by EU performers"), L.F("of the {0} Portuguese songs only · at least {1:0} %", f.Portuguese.Count, Erc.LanguageMinimum), f.Language, base_));
        panel.Children.Add(QuotaRow(L.T("Recent (last 12 months)"), L.F("of the {0} Portuguese songs only · at least {1:0} %", f.Portuguese.Count, Erc.RecentMinimum), f.Recent, base_));
        if (!base_ && f.Portuguese.Of > 0)
            panel.Children.Add(new TextBlock
            {
                Text = L.T("These two count only the Portuguese music: they mean little until its 30 % is reached. Songs not classified don't count as Portuguese."),
                Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            });
    }

    static readonly IBrush Good = new SolidColorBrush(Color.Parse("#3ECF8E")), Bad = new SolidColorBrush(Color.Parse("#E5484D")),
        Muted = new SolidColorBrush(Color.Parse("#8B93A3")), Track = new SolidColorBrush(Color.Parse("#262A33"));

    /// <summary>
    /// A quota: its name, the percentage (green when met), a bar with the minimum marked. <paramref name="counts"/> false:
    /// the quota it is a share of isn't met, so it is shown greyed, without ✓.
    /// </summary>
    static Control QuotaRow(string title, string detail, ErcQuota q, bool counts = true)
    {
        var brush = q.Of == 0 || (!counts && q.Met) ? Muted : q.Met ? Good : Bad;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        grid.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        var value = new TextBlock
        {
            Text = q.Of == 0 ? "—" : $"{q.Percent:0.0} %  {(!q.Met ? "✗" : counts ? "✓" : "")}", Foreground = brush, FontSize = 18, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(value, 1);
        Grid.SetRowSpan(value, 2);
        grid.Children.Add(value);
        var sub = new TextBlock { Text = $"{detail} · {q.Count}/{q.Of}", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(sub, 1);
        grid.Children.Add(sub);
        // The bar: 0–100 %, the minimum as a white mark
        var bar = new Panel { Height = 6, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        bar.Children.Add(new Border { Background = Track, CornerRadius = new Avalonia.CornerRadius(3) });
        var fill = new Border { Background = brush, CornerRadius = new Avalonia.CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        var mark = new Border { Background = Brushes.White, Width = 2, HorizontalAlignment = HorizontalAlignment.Left };
        bar.Children.Add(fill);
        bar.Children.Add(mark);
        bar.SizeChanged += (_, e) =>
        {
            fill.Width = e.NewSize.Width * Math.Clamp(q.Percent / 100, 0, 1);
            mark.Margin = new Avalonia.Thickness(e.NewSize.Width * q.Minimum / 100 - 1, -2, 0, -2);
        };
        Grid.SetRow(bar, 2);
        Grid.SetColumnSpan(bar, 2);
        grid.Children.Add(bar);
        return grid;
    }

    void ShowMissing(IEnumerable<(ErcPlay Play, ErcSong? Song)> plays)
    {
        var rows = plays.Select(p => (p.Play, p.Song, Missing: Erc.Missing(p.Song))).Where(p => p.Missing.Count > 0)
            .GroupBy(p => p.Play.Path)
            .Select(g =>
            {
                var (play, song, missing) = g.First();
                string name = string.IsNullOrWhiteSpace(play.Artist) ? play.Title : $"{play.Artist} - {play.Title}";
                string what = string.Join(", ", missing.Select(L.T));
                return (Count: g.Count(), Row: new MissingRow(song?.TrackId, play.Path, L.F("{0}×  {1}  —  {2}", g.Count(), name, what)));
            })
            .OrderByDescending(x => x.Count).Select(x => x.Row).ToList();
        MissingList.ItemsSource = rows;
        SelectAllButton.IsEnabled = rows.Count > 0;
    }

    List<MissingRow> SelectedRows() => MissingList.SelectedItems?.OfType<MissingRow>().ToList() ?? [];

    void Selected()
    {
        var rows = SelectedRows();
        ClassifyButton.IsEnabled = rows.Count > 0;
        LookupButton.IsEnabled = rows.Any(r => r.TrackId != null);
        AddToLibraryButton.IsEnabled = rows.Any(r => r.TrackId == null) && AddCategoryBox.SelectedItem != null;
    }

    /// <summary>A category to add songs to, as listed (Music › 80s).</summary>
    sealed record CategoryChoice(long Id, string Text)
    {
        public override string ToString() => Text;
    }

    void ShowCategories()
    {
        if (_db is not { } db) return;
        try
        {
            var all = db.Categories();
            var music = CategoryTree.Flatten(all).Where(c => c.Category.Kind == "music")
                .Select(c => new CategoryChoice(c.Category.Id, CategoryTree.PathOf(all, c.Category.Id))).ToList();
            AddCategoryBox.ItemsSource = music;
            AddCategoryBox.SelectedItem = music.FirstOrDefault(c => all.First(x => x.Id == c.Id).Role == "music") ?? music.FirstOrDefault();
        }
        catch { /* the library can't be read: the list stays empty */ }
        AddCategoryBox.SelectionChanged += (_, _) => Selected();
    }

    /// <summary>Songs that went on air from files outside the library: into the library, in the category chosen.</summary>
    async Task AddToLibrary()
    {
        if (_db is not { } db || AddCategoryBox.SelectedItem is not CategoryChoice category) return;
        var paths = SelectedRows().Where(r => r.TrackId == null).Select(r => r.Path).Distinct().ToList();
        AddToLibraryButton.IsEnabled = false;
        StatusText.Text = L.T("Adding to the library…");
        try
        {
            var (added, gone) = await Task.Run(() =>
            {
                var ids = new List<long>();
                int missing = 0;
                foreach (var played in paths)
                {
                    if (!SharedPaths.FileExists(played)) { missing++; continue; }
                    string path = SharedPaths.ToLibrary(Path.GetFullPath(SharedPaths.ToLocal(played)));
                    db.Refresh(path);
                    if (db.GetByPath(path) is { } track) ids.Add(track.Id);
                }
                if (ids.Count > 0) db.SetCategory(ids, category.Id);
                return (ids.Count, missing);
            });
            await Check();
            StatusText.Text = L.F("{0} songs added to {1}.", added, category.Text)
                              + (gone > 0 ? " " + L.F("{0} files no longer exist.", gone) : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Not added: {0}", ex.Message);
        }
    }

    async Task LookUp()
    {
        if (_db is not { } db || TopLevel.GetTopLevel(this) is not Window owner) return;
        var ids = SelectedRows().Select(r => r.TrackId).OfType<long>().Distinct().ToList();
        if (ids.Count == 0) return;
        if (await new ErcLookupWindow(db, ids, _countries).ShowDialog<bool>(owner)) await Check();
    }

    async Task Classify()
    {
        if (_db is not { } db || TopLevel.GetTopLevel(this) is not Window owner) return;
        var ids = MissingList.SelectedItems?.OfType<MissingRow>().Select(r => r.TrackId).OfType<long>().Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            StatusText.Text = L.T("These songs aren't in the library: add them first (Add to the library, below).");
            return;
        }
        if (await new ErcSongWindow(db, ids, _countries).ShowDialog<bool>(owner)) await Check();
    }

    // ───────────────────────── The ERC's file ─────────────────────────

    async Task Export()
    {
        if (_db is null || TopLevel.GetTopLevel(this) is not { } top) return;
        if (Period() != _checked) await Check();
        if (_played.Count == 0)
        {
            StatusText.Text = L.T("No songs went on air in this period.");
            return;
        }
        var (from, to) = _checked;
        string alias = Alias();
        string days = from.Day == 1 && to == from.AddMonths(1).AddDays(-1) ? $"{from:yyyy-MM}" : $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Export for the ERC"),
            SuggestedFileName = $"ERC-{string.Concat(alias.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-')}-{days}.xml",
            DefaultExtension = "xml",
            FileTypeChoices = [new FilePickerFileType("XML") { Patterns = ["*.xml"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await using (var stream = File.Create(path)) Erc.WriteXml(stream, alias, _played);
            int unclassified = _played.Count(p => p.Song?.Portuguese == null);
            StatusText.Text = L.F("Saved {0}: {1} songs.", Path.GetFileName(path), _played.Count)
                              + (unclassified > 0 ? " " + L.F("{0} of them not classified (sent as not Portuguese music).", unclassified) : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }

    string Alias() => AliasBox.Text?.Trim() is { Length: > 0 } a ? a : _alias;

    // ───────────────────────── The station's details ─────────────────────────

    async Task LoadDetails()
    {
        if (_db is not { } db) return;
        try
        {
            var (station, _) = await Task.Run(() => new StationStore(db).Load());
            _alias = station?.StationName ?? "";
            AliasBox.Text = station?.ErcAlias is { Length: > 0 } a ? a : _alias;
            _countries = station?.ErcCountries is { Count: > 0 } list ? list : Erc.DefaultCountries();
            PlanBox.IsChecked = station?.ErcPlan != false;
        }
        catch (Exception ex)
        {
            DetailsText.Text = L.F("The station's settings can't be read: {0}", ex.Message);
        }
        ShowCountries();
    }

    void ShowCountries()
    {
        CountryPanel.Children.Clear();
        foreach (var country in _countries)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,64,6,Auto,6,Auto") };
            var name = new TextBox { Text = country.Name, PlaceholderText = L.T("Country") };
            name.TextChanged += (_, _) => country.Name = name.Text ?? "";
            var code = new TextBox { Text = country.Code, PlaceholderText = L.T("Code") };
            code.TextChanged += (_, _) => country.Code = code.Text ?? "";
            var eu = new CheckBox { Content = L.T("EU"), IsChecked = country.Eu };
            eu.IsCheckedChanged += (_, _) => country.Eu = eu.IsChecked == true;
            var remove = new Button { Content = "✕", Padding = new Avalonia.Thickness(8, 4) };
            remove.Click += (_, _) => { _countries.Remove(country); ShowCountries(); };
            Grid.SetColumn(code, 2);
            Grid.SetColumn(eu, 4);
            Grid.SetColumn(remove, 6);
            row.Children.Add(name);
            row.Children.Add(code);
            row.Children.Add(eu);
            row.Children.Add(remove);
            CountryPanel.Children.Add(row);
        }
    }

    async Task SaveDetails()
    {
        if (_db is not { } db) return;
        var countries = _countries.Where(c => c.Name.Trim().Length > 0 || c.Code.Trim().Length > 0)
            .Select(c => new ErcCountry { Name = c.Name.Trim(), Code = c.Code.Trim(), Eu = c.Eu }).ToList();
        if (countries.Any(c => c.Name.Length == 0 || c.Code.Length == 0))
        {
            DetailsText.Text = L.T("Each country needs a name and its ERC code.");
            return;
        }
        if (countries.GroupBy(c => c.Code).Any(g => g.Count() > 1))
        {
            DetailsText.Text = L.T("Two countries have the same code.");
            return;
        }
        string alias = AliasBox.Text?.Trim() ?? "";
        bool plan = PlanBox.IsChecked == true;
        try
        {
            await Task.Run(() =>
            {
                var store = new StationStore(db);
                var (station, version) = store.Load();
                station ??= StationSettings.From(AppSettings.Load());
                station.ErcAlias = alias;
                station.ErcCountries = countries;
                station.ErcPlan = plan;
                store.Save(station, version, _who);
            });
            _countries = countries;
            ShowCountries();
            DetailsText.Text = L.F("Saved at {0:HH:mm:ss}.", DateTime.Now);
            await Check();
        }
        catch (Exception ex)
        {
            DetailsText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }
}
