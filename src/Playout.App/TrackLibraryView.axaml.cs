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

        TrackList.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is LibraryRow row) AddRequested?.Invoke([row.Path], false);
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
            rows => DragHelper.Data(AudioFiles.DragFormat, rows.Select(r => r.Path).ToArray()));
    }

    internal void Attach(LibraryService service)
    {
        _service = service;
        service.Changed += Reload;
        Reload();
    }

    List<LibraryRow> Selected() => TrackList.SelectedItems?.OfType<LibraryRow>().ToList() ?? new();

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
            filters.AddRange(categories.Select(c => new FilterItem(c.Name, new CategoryFilter(c.Id))));
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
        var categories = _categories.ToDictionary(c => c.Id);
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
            var rows = page.Tracks.Select(x => new LibraryRow(x, x.CategoryId is { } id ? categories.GetValueOrDefault(id) : null)).ToList();
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

        menu.Items.Add(Item(L.T("Add to playlist"), () => AddRequested?.Invoke(paths, false)));
        menu.Items.Add(Item(L.T("Play next"), () => AddRequested?.Invoke(paths, true)));
        menu.Items.Add(new Separator());

        var category = new MenuItem { Header = L.T("Category") + many };
        long? current = rows.Select(r => r.CategoryId).Distinct().Count() == 1 ? rows[0].CategoryId : -1;
        foreach (var c in _categories)
            category.Items.Add(Item(Mark(c.Name, current == c.Id), () => Edit(db => db.SetCategory(ids, c.Id))));
        category.Items.Add(new Separator());
        category.Items.Add(Item(Mark(L.T("None"), current == null), () => Edit(db => db.SetCategory(ids, null))));
        category.Items.Add(new Separator());
        category.Items.Add(Item(L.T("Edit categories…"), async () => await EditCategories()));
        menu.Items.Add(category);

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
            var text = await Dialogs.AskText(owner, L.T("Language"), L.T("Language code, e.g. pt, en, es (empty: not set)"), initial,
                t => t.Length > 10 ? L.T("Use a short code such as pt or en") : null);
            if (text == null) return;
            Edit(db => db.SetAttributes(ids, text.Length == 0
                ? new TrackAttributes(ClearLanguage: true)
                : new TrackAttributes(Language: text.ToLowerInvariant())));
        }));

        bool allEnabled = rows.All(r => r.Track.Enabled);
        menu.Items.Add(Item(Mark(L.T("Never pick automatically"), !allEnabled), () =>
            Edit(db => db.SetAttributes(ids, new TrackAttributes(Enabled: !allEnabled)))));

        if (rows.Count == 1)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(L.T("Edit tags…"), () => EditTagsRequested?.Invoke(paths[0])));
            menu.Items.Add(Item(L.T("Edit cue points…"), () => EditCuesRequested?.Invoke(paths[0])));
        }
    }

    async Task EditCategories()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _service == null) return;
        await new CategoriesWindow(_service).ShowDialog(owner);
    }

    static string Mark(string label, bool on) => on ? $"{label}  ✓" : label;

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
public sealed class LibraryRow(LibraryTrack track, LibraryCategory? category)
{
    public LibraryTrack Track { get; } = track;
    public long Id => Track.Id;
    public long? CategoryId => Track.CategoryId;
    public string Path => Track.Path;
    public string Title => Track.Title;
    public string Artist => Track.Artist.Length > 0 ? Track.Artist : "—";
    public string Length => Format.Duration(Track.Duration);
    public string Category { get; } = category?.Name ?? "";
    public bool HasCategory => Category.Length > 0;
    public IBrush CategoryBrush { get; } =
        category?.Color is { } c && Color.TryParse(c, out var color) ? new SolidColorBrush(color) : new SolidColorBrush(Color.Parse("#4A5160"));
}
