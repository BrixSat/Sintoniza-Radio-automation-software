using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The music library: search as you type, filter by category, and drag or double-click songs into the playlist.
/// Right-click sets the category and attributes (tempo, voice, language) of the selected songs.
/// </summary>
public partial class TrackLibraryView : UserControl
{
    const int PageSize = 1000; // more than anyone scrolls through; search narrows it down

    static readonly (string Label, string? Value)[] Voices =
        [(L.T("Male"), "male"), (L.T("Female"), "female"), (L.T("Duo"), "duo"), (L.T("Group"), "group"), (L.T("Instrumental"), "instrumental")];

    LibraryService? _service;
    IReadOnlyList<LibraryCategory> _categories = [];
    readonly DispatcherTimer _searchDelay;
    int _queryVersion;

    /// <summary>Paths to add; <c>asNext</c> means "play after the current item".</summary>
    public event Action<IReadOnlyList<string>, bool>? AddRequested;
    public event Action<string>? EditCuesRequested;
    public event Action<string>? EditTagsRequested;
    /// <summary>Pre-listen to a song (offered in the menu only where something listens, e.g. the playlist manager).</summary>
    public event Action<string>? ListenRequested;

    public TrackLibraryView()
    {
        InitializeComponent();
        Localization.Apply(this);
        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _searchDelay!.Stop();
            Query();
        });
        SearchBox.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };
        CategoryBox.SelectionChanged += (_, _) => Query();
        EditCategoriesButton.Click += async (_, _) => await EditCategories();
        RefreshButton.Click += (_, _) => _service?.NotifyChanged(); // everything showing the library reads it again

        TrackList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is not LibraryRow row) return;
            // On air it goes in the playlist; in Sintoniza Pro (nothing to add to) it is listened to.
            if (AddRequested != null) AddRequested.Invoke([row.Path], false);
            else ListenRequested?.Invoke(row.Path);
        };
        TrackList.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Selected() is not { Count: > 0 } rows) return;
            AddRequested?.Invoke(rows.Select(r => r.Path).ToList(), false);
            e.Handled = true;
        };
        TrackList.ContextMenu = new ContextMenu();
        TrackList.ContextMenu.Opening += (_, e) =>
        {
            if (Selected().Count == 0) { e.Cancel = true; return; }
            BuildMenu(TrackList.ContextMenu);
        };
        DragHelper.Attach(TrackList, Selected,
            rows => DragHelper.Paths(rows.Select(r => r.Path)));
    }

    internal void Attach(LibraryService service)
    {
        _service = service;
        service.Changed += Reload;
        Reload();
    }

    /// <summary>Puts the cursor in the search box, all text selected (Ctrl/Cmd+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    List<LibraryRow> Selected() => TrackList.SelectedItems?.OfType<LibraryRow>().ToList() ?? new();

    /// <summary>The song selected (the first of them), its file and "Artist - Title"; null: none.</summary>
    public (string Path, string Title)? SelectedSong =>
        Selected() is [var r, ..] ? (r.Path, r.Track.Artist.Length > 0 ? $"{r.Track.Artist} - {r.Track.Title}" : r.Track.Title) : null;

    // ───────────────────────── Loading ─────────────────────────

    /// <summary>The library opened, or its contents changed: reload the categories and the list.</summary>
    void Reload()
    {
        if (_service?.Db is not { } db)
        {
            ShowEmpty(_service?.Error ?? L.T("Opening the library…"));
            CategoryBox.ItemsSource = null;
            StatusText.Text = "";
            return;
        }
        Task.Run(() => (db.Categories(), db.Stats())).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                ShowEmpty(L.F("Library unavailable: {0}", t.Exception?.InnerException?.Message));
                return;
            }
            var (categories, stats) = t.Result;
            _categories = categories;
            var previous = (CategoryBox.SelectedItem as FilterItem)?.Name;
            var filters = new List<FilterItem> { new(L.T("All"), CategoryFilter.All) };
            // A category takes in the ones under it: shown indented beneath it.
            filters.AddRange(CategoryTree.Flatten(categories).Select(x => new FilterItem(new string(' ', 4 * x.Depth) + x.Category.Name, new CategoryFilter(x.Category.Id))));
            filters.Add(new FilterItem(L.T("No category"), new CategoryFilter(Uncategorised: true)));
            filters.Add(new FilterItem(L.T("Missing files"), new CategoryFilter(MissingFiles: true)));
            CategoryBox.ItemsSource = filters;
            CategoryBox.SelectedItem = filters.FirstOrDefault(f => f.Name == previous) ?? filters[0];
            StatusText.Text = stats.Tracks == 0 ? "" :
                L.F("{0:N0} songs · {1:N0} h", stats.Tracks, stats.Hours) +
                (stats.Uncategorised > 0 ? L.F(" · {0:N0} without category", stats.Uncategorised) : "") +
                (stats.Missing > 0 ? L.F(" · {0:N0} missing", stats.Missing) : "");
            Query();
        }));
    }

    void Query()
    {
        if (_service?.Db is not { } db) return;
        var filter = (CategoryBox.SelectedItem as FilterItem)?.Filter ?? CategoryFilter.All;
        string text = SearchBox.Text ?? "";
        int version = ++_queryVersion;
        var all = _categories;
        Task.Run(() => db.Search(text, filter, PageSize)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (version != _queryVersion) return; // typed on meanwhile
            if (!t.IsCompletedSuccessfully)
            {
                ShowEmpty(L.F("Library unavailable: {0}", t.Exception?.InnerException?.Message));
                return;
            }
            var page = t.Result;
            var selected = Selected().Select(r => r.Id).ToHashSet();
            var rows = page.Tracks.Select(x => new LibraryRow(x, all)).ToList();
            TrackList.ItemsSource = rows;
            foreach (var row in rows.Where(r => selected.Contains(r.Id))) TrackList.SelectedItems?.Add(row);
            CountText.Text = page.Total > rows.Count ? L.F("{0:N0} of {1:N0}", rows.Count, page.Total) : L.F("{0:N0}", page.Total);
            if (page.Total == 0)
                ShowEmpty(string.IsNullOrWhiteSpace(text) && filter == CategoryFilter.All
                    ? L.T("The library is empty.\nAdd your music folders in Settings → Library and click Scan now.")
                    : L.T("Nothing found."));
            else EmptyText.IsVisible = false;
        }));
    }

    void ShowEmpty(string message)
    {
        TrackList.ItemsSource = null;
        CountText.Text = "";
        EmptyText.Text = message;
        EmptyText.IsVisible = true;
    }

    // ───────────────────────── Menu ─────────────────────────

    void BuildMenu(ContextMenu menu)
    {
        var rows = Selected();
        var paths = rows.Select(r => r.Path).ToList();
        var ids = rows.Select(r => r.Id).ToList();
        string many = rows.Count > 1 ? L.F(" ({0} songs)", rows.Count) : "";
        menu.Items.Clear();

        if (ListenRequested != null && rows.Count == 1)
        {
            menu.Items.Add(Item(L.T("Listen"), () => ListenRequested?.Invoke(paths[0])));
            menu.Items.Add(new Separator());
        }
        // A playlist to add to (on air, the daily playlists); none in Sintoniza Pro's Songs.
        if (AddRequested != null)
        {
            menu.Items.Add(Item(L.T("Add to playlist"), () => AddRequested?.Invoke(paths, false)));
            menu.Items.Add(Item(L.T("Play next"), () => AddRequested?.Invoke(paths, true)));
            menu.Items.Add(new Separator());
        }

        var tree = CategoryTree.Flatten(_categories);
        var category = new MenuItem { Header = L.T("Category") + many };
        long? current = rows.Select(r => r.CategoryId).Distinct().Count() == 1 ? rows[0].CategoryId : -1;
        foreach (var (c, depth) in tree)
            category.Items.Add(Item(Mark(Indent(c.Name, depth), current == c.Id), () => Edit(db => db.SetCategory(ids, c.Id))));
        category.Items.Add(new Separator());
        category.Items.Add(Item(Mark(L.T("None"), current == null), () => Edit(db => db.SetCategory(ids, null))));
        if (CanEditCategories)
        {
            category.Items.Add(new Separator());
            category.Items.Add(Item(L.T("Edit categories…"), async () => await EditCategories()));
        }
        menu.Items.Add(category);

        // Other categories the songs are in too (a song can be in several): ticked when all of them are.
        var also = new MenuItem { Header = L.T("Also in") + many };
        foreach (var (c, depth) in tree)
        {
            bool all = rows.All(r => r.Track.AlsoIn.Contains(c.Id) || r.CategoryId == c.Id);
            var targets = rows.Where(r => r.CategoryId != c.Id).Select(r => r.Id).ToList();
            var item = Item(Mark(Indent(c.Name, depth), all), () => Edit(db => db.SetAlsoIn(targets, c.Id, !all)));
            item.IsEnabled = targets.Count > 0;
            also.Items.Add(item);
        }
        menu.Items.Add(also);

        var tempo = new MenuItem { Header = L.T("Tempo") + many };
        string[] tempos = [L.T("1 · Slow"), "2", L.T("3 · Medium"), "4", L.T("5 · Fast")];
        int? currentTempo = rows.Select(r => r.Track.Tempo).Distinct().Count() == 1 ? rows[0].Track.Tempo : -1;
        for (int i = 0; i < tempos.Length; i++)
        {
            int value = i + 1;
            tempo.Items.Add(Item(Mark(tempos[i], currentTempo == value), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(Tempo: value)))));
        }
        tempo.Items.Add(new Separator());
        tempo.Items.Add(Item(L.T("Not set"), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(ClearTempo: true)))));
        menu.Items.Add(tempo);

        var energy = new MenuItem { Header = L.T("Energy") + many };
        string[] energies = [L.T("1 · Calm"), "2", "3", "4", L.T("5 · Lively")];
        int? currentEnergy = rows.Select(r => r.Track.Energy).Distinct().Count() == 1 ? rows[0].Track.Energy : -1;
        for (int i = 0; i < energies.Length; i++)
        {
            int value = i + 1;
            energy.Items.Add(Item(Mark(energies[i], currentEnergy == value), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(Energy: value)))));
        }
        energy.Items.Add(new Separator());
        energy.Items.Add(Item(L.T("Not set"), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(ClearEnergy: true)))));
        menu.Items.Add(energy);

        var voice = new MenuItem { Header = L.T("Voice") + many };
        string? currentVoice = rows.Select(r => r.Track.Voice).Distinct().Count() == 1 ? rows[0].Track.Voice : "?";
        foreach (var (label, value) in Voices)
            voice.Items.Add(Item(Mark(label, currentVoice == value), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(Voice: value)))));
        voice.Items.Add(new Separator());
        voice.Items.Add(Item(L.T("Not set"), () => Edit(db => db.SetAttributes(ids, new TrackAttributes(ClearVoice: true)))));
        menu.Items.Add(voice);

        menu.Items.Add(Item(L.T("Language") + many + "…", async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            string initial = rows.Select(r => r.Track.Language).Distinct().Count() == 1 ? rows[0].Track.Language ?? "" : "";
            var text = await Dialogs.AskText(owner, L.T("Language"), L.T("Language code, e.g. pt-PT, pt-BR, en, es (empty: not set)"), initial,
                t => t.Length > 10 ? L.T("Use a short code such as pt-PT or en") : null);
            if (text == null) return;
            // pt-pt → pt-PT, EN → en
            string code = text.Trim().Replace('_', '-') is var t2 && t2.Split('-', 2) is [var lang, var region] ? $"{lang.ToLowerInvariant()}-{region.ToUpperInvariant()}" : text.Trim().ToLowerInvariant();
            Edit(db => db.SetAttributes(ids, code.Length == 0
                ? new TrackAttributes(ClearLanguage: true)
                : new TrackAttributes(Language: code)));
        }));

        menu.Items.Add(Item(L.T("Restrictions") + many + "…", async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || _service?.Db is not { } db) return;
            if (await new RestrictionsWindow(db, ids).ShowDialog<bool>(owner)) _service.NotifyChanged();
        }));
        static async Task<List<ErcCountry>> Countries(LibraryDb db)
        {
            try { return (await Task.Run(() => new StationStore(db).Load().Settings?.ErcCountries)) ?? Erc.DefaultCountries(); }
            catch { return Erc.DefaultCountries(); }
        }
        menu.Items.Add(Item(L.T("ERC classification") + many + "…", async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || _service?.Db is not { } db) return;
            if (await new ErcSongWindow(db, ids, await Countries(db)).ShowDialog<bool>(owner)) _service.NotifyChanged();
        }));
        menu.Items.Add(Item(L.T("ERC: look up on MusicBrainz") + many + "…", async () =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || _service?.Db is not { } db) return;
            if (await new ErcLookupWindow(db, ids, await Countries(db)).ShowDialog<bool>(owner)) _service.NotifyChanged();
        }));

        bool allEnabled = rows.All(r => r.Track.Enabled);
        menu.Items.Add(Item(Mark(L.T("Never pick automatically"), !allEnabled), () =>
            Edit(db => db.SetAttributes(ids, new TrackAttributes(Enabled: !allEnabled)))));

        if (rows.Count == 1)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(L.T("Edit tags…"), () => EditTagsRequested?.Invoke(paths[0])));
            if (EditCuesRequested != null) menu.Items.Add(Item(L.T("Edit cue points…"), () => EditCuesRequested?.Invoke(paths[0])));
        }
    }

    /// <summary>The folders of each category, for the editor (set by the main window).</summary>
    internal Func<CategoryFolders>? CategoryFolders { get; set; }

    /// <summary>The categories can be edited from here (false in the playout: they are Sintoniza Pro's).</summary>
    public bool CanEditCategories
    {
        get => EditCategoriesButton.IsVisible;
        set => EditCategoriesButton.IsVisible = value;
    }

    async Task EditCategories()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _service == null) return;
        await new CategoriesWindow(_service, CategoryFolders?.Invoke()).ShowDialog(owner);
    }

    static string Mark(string label, bool on) => on ? $"{label}  ✓" : label;

    static string Indent(string name, int depth) => new string(' ', 4 * depth) + name;

    static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>Saves a change in the background, then shows the list again.</summary>
    void Edit(Action<LibraryDb> change)
    {
        if (_service?.Db is not { } db) return;
        Task.Run(() => change(db)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (!t.IsCompletedSuccessfully) StatusText.Text = L.F("Not saved: {0}", t.Exception?.InnerException?.Message);
            _service.NotifyChanged();
        }));
    }

    sealed record FilterItem(string Name, CategoryFilter Filter)
    {
        public override string ToString() => Name;
    }
}

/// <summary>A song in the library list.</summary>
public sealed class LibraryRow(LibraryTrack track, IReadOnlyList<LibraryCategory> categories)
{
    readonly LibraryCategory? _category = categories.FirstOrDefault(c => c.Id == track.CategoryId);

    public LibraryTrack Track { get; } = track;
    public long Id => Track.Id;
    public long? CategoryId => Track.CategoryId;
    public string Path => Track.Path;
    public string Title => Track.Title;
    public string Artist => Track.Artist.Length > 0 ? Track.Artist : "—";
    public string Length => Format.Duration(Track.Duration);
    public string Year => Track.Year is { } y and > 0 ? y.ToString() : "";
    /// <summary>Its own category, and "+2" when it is in two more.</summary>
    public string Category => _category == null ? "" : Track.AlsoIn.Count > 0 ? $"{_category.Name} +{Track.AlsoIn.Count}" : _category.Name;
    public bool HasCategory => Category.Length > 0;
    /// <summary>The title, marked ⏱ when the song has restrictions.</summary>
    public string ShownTitle => Track.Restrictions != null ? "⏱ " + Track.Title : Track.Title;
    /// <summary>The album, its tempo and energy, every category it is in (full names), and the file, on hover.</summary>
    public string Tip
    {
        get
        {
            var lines = new List<string>();
            if (Track.Album.Length > 0) lines.Add(Track.Album);
            if (Track.Tempo != null || Track.Energy != null)
                lines.Add(string.Join(" · ", new[] { Track.Tempo is { } t ? L.F("Tempo {0}", t) : null, Track.Energy is { } e ? L.F("Energy {0}", e) : null }.OfType<string>()));
            var all = (Track.CategoryId is { } own ? [own] : Array.Empty<long>()).Concat(Track.AlsoIn)
                .Select(id => CategoryTree.PathOf(categories, id)).Where(n => n.Length > 0).ToList();
            if (all.Count > 0) lines.Add(string.Join(", ", all));
            if (Track.Restrictions is { } r) lines.Add("⏱ " + Describe(r));
            lines.Add(Track.Path);
            return string.Join("\n", lines);
        }
    }
    /// <summary>A song's restrictions in words: "from 1/12 · until 6/1 · Mon–Fri · 07–19h · 2× a day".</summary>
    public static string Describe(SongRestrictions r)
    {
        var parts = new List<string>();
        if (r.From is { } from) parts.Add(L.F("from {0:d}", from));
        if (r.Until is { } until) parts.Add(L.F("until {0:d}", until));
        if (r.Days is { } days)
        {
            var names = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
            parts.Add(string.Join(" ", Enumerable.Range(0, 7).Where(i => days[i] == '1').Select(i => names.GetAbbreviatedDayName((DayOfWeek)((i + 1) % 7)))));
        }
        if (r.Hours is { } hours)
        {
            // Runs of hours: 07–19h, 22–23h
            var runs = new List<string>();
            for (int h = 0; h < 24; h++)
            {
                if (hours[h] != '1') continue;
                int end = h;
                while (end + 1 < 24 && hours[end + 1] == '1') end++;
                runs.Add(end == h ? $"{h:00}h" : $"{h:00}–{end:00}h");
                h = end;
            }
            parts.Add(string.Join(", ", runs));
        }
        if (r.MaxPerDay is { } d) parts.Add(L.F("{0}× a day", d));
        if (r.MaxPerWeek is { } w) parts.Add(L.F("{0}× a week", w));
        return string.Join(" · ", parts);
    }

    public IBrush CategoryBrush => _category?.Color is { } c && Color.TryParse(c, out var color) ? new SolidColorBrush(color) : new SolidColorBrush(Color.Parse("#4A5160"));
}
