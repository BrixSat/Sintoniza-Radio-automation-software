using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Playout.App.Controls;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>One line in the schedule window's lists.</summary>
public sealed class ScheduleRow : INotifyPropertyChanged
{
    readonly Func<(string Title, string Detail, bool On)> _describe;

    public ScheduleRow(object item, Func<(string, string, bool)> describe)
    {
        Item = item;
        _describe = describe;
    }

    public object Item { get; }
    public string Title => _describe().Title;
    public string Detail => _describe().Detail;
    public double Opacity => _describe().On ? 1 : 0.45;

    public void Refresh()
    {
        foreach (var name in new[] { nameof(Title), nameof(Detail), nameof(Opacity) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The schedule program (<c>--schedule</c>, its own menu entry, apart from the on-air screen): events at set times
/// (news, programmes, streams), ad breaks and their running order, and the clients' campaigns placed in them, on week
/// calendars. Save writes schedule.json; the playout loads it within a few seconds.
/// </summary>
public partial class ScheduleWindow : Window
{
    static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    static readonly int[] Steps = [5, 10, 15, 20, 30, 60];

    readonly ScheduleData _data;
    readonly string _browseFolder;
    readonly EntryCalendar _eventCalendar, _breakCalendar, _templateCalendar;
    readonly Dictionary<string, double> _lengths = new(); // advert file → seconds
    List<ScheduleRow> _eventRows = new(), _breakRows = new(), _clientRows = new(), _campaignRows = new(), _templateRows = new();
    List<LibraryCategory> _categories = new();
    DateTime _week = Monday(DateTime.Today);
    List<double> _spotTimes = new();
    List<AdPlacement>? _spotSnapshot;
    bool _loading, _closing;
    string _saved;

    /// <summary>The schedule as text, to tell whether it changed since it was opened or saved.</summary>
    string Snapshot()
    {
        var state = _data.LastStarted;
        _data.LastStarted = new();
        try { return System.Text.Json.JsonSerializer.Serialize(_data); }
        finally { _data.LastStarted = state; }
    }

    bool Changed => _saved != null && Snapshot() != _saved;

    // Parameterless constructor for the XAML previewer.
    public ScheduleWindow() : this(new ScheduleData(), "") { }

    public ScheduleWindow(ScheduleData data, string browseFolder) : this(data, browseFolder, null, 0, null) { }

    readonly ScheduleStore? _store;
    long _version;
    readonly string? _offline;

    /// <param name="store">The database the schedule is saved to; null: it can't be saved (<paramref name="offline"/> says why).</param>
    /// <param name="version">The version <paramref name="data"/> was read at: a save over a newer one asks first.</param>
    readonly LibraryService? _library;
    readonly AppSettings? _settings;
    DailyPlaylistsView? _daily;

    /// <param name="library">The music library (the daily playlists' library panel); null: that tab isn't offered.</param>
    internal ScheduleWindow(ScheduleData data, string browseFolder, ScheduleStore? store, long version, string? offline,
        LibraryService? library = null, AppSettings? settings = null, SintonizaUser? user = null)
    {
        _user = user;
        _library = library;
        _settings = settings;
        InitializeComponent();
        Localization.Apply(this);
        _data = data;
        _data.Normalize();
        _browseFolder = browseFolder;
        _store = store;
        _version = version;
        _offline = offline;
        if (offline != null)
        {
            ErrorText.Text = offline;
            SaveButton.IsEnabled = false;
        }

        _eventCalendar = new EntryCalendar(this, EventGrid, EventHeader, EventStepBox, EventOffsetBox, EventAddTimeBox, EventAddTimeButton, EventClearButton,
            () => _data.Events, () => (EventList.SelectedItem as ScheduleRow)?.Item as ScheduledEvent, () => (EventList.SelectedItem as ScheduleRow)?.Refresh());
        _breakCalendar = new EntryCalendar(this, BreakGrid, BreakHeader, BreakStepBox, BreakOffsetBox, BreakAddTimeBox, BreakAddTimeButton, BreakClearButton,
            () => _data.Breaks, () => SelectedBreak, () =>
            {
                (BreakList.SelectedItem as ScheduleRow)?.Refresh();
                ShowBooked();
                ShowSpotWeek();
                ShowPlaced(); // a break moved: the campaigns' places left without one
            });

        _templateCalendar = new EntryCalendar(this, TemplateGrid, TemplateHeader, TemplateStepBox, TemplateOffsetBox, TemplateAddTimeBox, TemplateAddTimeButton, TemplateClearButton,
            () => _data.Templates, () => SelectedTemplate, () => (TemplateList.SelectedItem as ScheduleRow)?.Refresh());
        TemplateStepBox.SelectedIndex = Steps.Length - 1; // a row an hour

        WireEvents();
        WireTemplates();
        WireBreaks();
        WireClients();
        SaveButton.Click += (_, _) => Save();
        WireStation(store);
        WirePro(store);
        // The daily playlists: made when the tab is first opened (it reads the database), only with one to read.
        DailyTab.IsVisible = store != null && library != null && settings != null;
        Tabs.SelectionChanged += (_, _) =>
        {
            if (Tabs.SelectedItem != DailyTab || _daily != null || store == null || settings == null) return;
            DailyTab.Content = _daily = new DailyPlaylistsView(store.Db, library, store, settings, _data.Clone(), null, ChangeNow) { VoiceOnly = !May(Permission.Programming) };
        };
        Closed += (_, _) => _daily?.Stop();
        Closing += async (_, e) =>
        {
            if (_closing || !Changed) return;
            e.Cancel = true;
            if (!await Dialogs.Confirm(this, L.T("Unsaved changes"), L.T("Close without saving the changes?"), L.T("Close"))) return;
            _closing = true;
            Close();
        };
        _saved = Snapshot();

        _eventRows = data.Events.Select(EventRow).ToList();
        _breakRows = data.Breaks.Select(BreakRow).ToList();
        _clientRows = data.Clients.Select(ClientRow).ToList();
        _templateRows = data.Templates.Select(TemplateRow).ToList();
        TemplateList.ItemsSource = _templateRows;
        TemplateList.SelectedIndex = _templateRows.Count > 0 ? 0 : -1;
        ShowTemplate();
        _ = LoadCategories();
        EventList.ItemsSource = _eventRows;
        BreakList.ItemsSource = _breakRows;
        ClientList.ItemsSource = _clientRows;
        SectionNameBox.ItemsSource = _data.Sections();
        EventList.SelectedIndex = _eventRows.Count > 0 ? 0 : -1;
        BreakList.SelectedIndex = _breakRows.Count > 0 ? 0 : -1;
        ClientList.SelectedIndex = _clientRows.Count > 0 ? 0 : -1;
        ShowEvent();
        ShowBreak();
        ShowClient();
        ShowSpotWeek();
        // Open at the morning, where most of the day's schedule is.
        Opened += (_, _) =>
        {
            foreach (var (scroll, grid) in new[] { (EventScroll, EventGrid), (BreakScroll, BreakGrid), (TemplateScroll, TemplateGrid) })
            {
                int row = grid.Times.ToList().FindIndex(t => t >= 6 * 3600);
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, row) * WeekGrid.RowHeight);
            }
        };
    }

    // ───────────────────────── Events ─────────────────────────

    void WireEvents()
    {
        EventList.SelectionChanged += (_, _) => ShowEvent();
        AddEventButton.Click += (_, _) =>
        {
            var e = new ScheduledEvent { Name = L.T("News") };
            _data.Events.Add(e);
            _eventRows = _eventRows.Append(EventRow(e)).ToList();
            EventList.ItemsSource = _eventRows;
            EventList.SelectedIndex = _eventRows.Count - 1;
            EventNameBox.Focus();
            EventNameBox.SelectAll();
        };
        DeleteEventButton.Click += async (_, _) =>
        {
            if (EventList.SelectedItem is not ScheduleRow { Item: ScheduledEvent e } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete event"), L.F("Delete \"{0}\"?", EntryName(e)), L.T("Delete"))) return;
            _data.Events.Remove(e);
            int i = _eventRows.IndexOf(row);
            _eventRows = _eventRows.Where(r => r != row).ToList();
            EventList.ItemsSource = _eventRows;
            EventList.SelectedIndex = Math.Min(i, _eventRows.Count - 1);
            _eventCalendar.Rebuild();
        };
        EventNameBox.TextChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.Name = EventNameBox.Text?.Trim() ?? "");
        EventEnabledBox.IsCheckedChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.Enabled = EventEnabledBox.IsChecked == true);
        EventPathBox.TextChanged += (_, _) =>
        {
            Edit<ScheduledEvent>(EventList, e => e.Path = EventPathBox.Text?.Trim() ?? "");
            ShowEventPathKind();
        };
        EventFileButton.Click += async (_, _) => { if (await PickFile(L.T("Event: file to play")) is { } p) EventPathBox.Text = p; };
        EventFolderButton.Click += async (_, _) => { if (await PickFolder(L.T("Event: folder whose newest file plays")) is { } p) EventPathBox.Text = p; };
        EventInputButton.Click += (_, _) => InputMenu(EventInputButton, path => EventPathBox.Text = path);
        EventMaxAgeBox.ValueChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.MaxAgeMinutes = (int)(EventMaxAgeBox.Value ?? 0));
        EventStreamMinutesBox.ValueChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.StreamMinutes = (double)(EventStreamMinutesBox.Value ?? 0));
        WireStart(EventList, EventSoftBox, EventHardBox, EventEndBox, EventDelayBox, EventLockBox, EventDelayPanel);
    }

    void ShowEvent()
    {
        var e = (EventList.SelectedItem as ScheduleRow)?.Item as ScheduledEvent;
        EventPanel.IsEnabled = e != null;
        _loading = true;
        EventNameBox.Text = e?.Name ?? "";
        EventEnabledBox.IsChecked = e?.Enabled ?? true;
        EventPathBox.Text = e?.Path ?? "";
        EventMaxAgeBox.Value = e?.MaxAgeMinutes ?? 0;
        EventStreamMinutesBox.Value = (decimal)(e?.StreamMinutes ?? 0);
        ShowStart(e, EventSoftBox, EventHardBox, EventEndBox, EventDelayBox, EventLockBox, EventDelayPanel);
        _loading = false;
        ShowEventPathKind();
        _eventCalendar.Rebuild();
    }

    /// <summary>What the path is: a file, a folder (newest file), a stream; shows the options that go with it.</summary>
    void ShowEventPathKind()
    {
        string path = EventPathBox.Text?.Trim() ?? "";
        bool stream = PlayoutEngine.IsStreamSource(path);
        bool folder = !stream && path.Length > 0 && SharedPaths.DirectoryExists(path);
        EventStreamPanel.IsVisible = stream;
        EventFolderPanel.IsVisible = folder;
        EventPathNote.Text = path.Length == 0 ? "" :
            LiveInput.Is(path) ? L.F("An input of the studio computer, live: {0}.", LiveInput.DeviceOf(path) is { Length: > 0 } d ? d : L.T("the default input")) :
            stream ? L.T("An internet stream.") :
            folder ? L.T("A folder: its newest audio file plays (e.g. a news bulletin that is downloaded there).") :
            SharedPaths.FileExists(path) ? L.T("A file.") : L.T("Not found on this computer.");
    }

    /// <summary>
    /// The inputs to choose from: this computer's, as names to look for on the studio computer (where the event plays),
    /// and its default input.
    /// </summary>
    static void InputMenu(Control anchor, Action<string> chosen)
    {
        var menu = new ContextMenu();
        var defaultItem = new MenuItem { Header = L.T("(the default input)") };
        defaultItem.Click += (_, _) => chosen(LiveInput.PathFor(""));
        menu.Items.Add(defaultItem);
        try
        {
            foreach (var device in PlayoutEngine.GetInputDevices())
            {
                var item = new MenuItem { Header = device };
                item.Click += (_, _) => chosen(LiveInput.PathFor(device));
                menu.Items.Add(item);
            }
        }
        catch { /* no audio library: the default only */ }
        menu.Open(anchor);
    }

    ScheduleRow EventRow(ScheduledEvent e) => new(e, () =>
    {
        string what = LiveInput.Is(e.Path) ? L.T("live input") : PlayoutEngine.IsUrl(e.Path) ? L.T("stream") : SharedPaths.DirectoryExists(e.Path) ? L.T("newest in folder") : Path.GetFileName(e.Path);
        return (EntryName(e), $"{SlotsSummary(e)} · {StartText(e)}{(what.Length > 0 ? " · " + what : "")}", e.Enabled);
    });

    // ───────────────────────── Playlists (smart playlists) ─────────────────────────

    PlaylistTemplate? SelectedTemplate => (TemplateList.SelectedItem as ScheduleRow)?.Item as PlaylistTemplate;
    TemplateLine? SelectedLine => (LineList.SelectedItem as ScheduleRow)?.Item as TemplateLine;

    void WireTemplates()
    {
        TemplateList.SelectionChanged += (_, _) => ShowTemplate();
        AddTemplateButton.Click += (_, _) =>
        {
            var t = new PlaylistTemplate { Name = L.T("New playlist") };
            _data.Templates.Add(t);
            _templateRows = _templateRows.Append(TemplateRow(t)).ToList();
            TemplateList.ItemsSource = _templateRows;
            TemplateList.SelectedIndex = _templateRows.Count - 1;
            TemplateNameBox.Focus();
            TemplateNameBox.SelectAll();
        };
        DeleteTemplateButton.Click += async (_, _) =>
        {
            if (TemplateList.SelectedItem is not ScheduleRow { Item: PlaylistTemplate t } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete playlist"), L.F("Delete \"{0}\"?", TemplateName(t)), L.T("Delete"))) return;
            _data.Templates.Remove(t);
            int i = _templateRows.IndexOf(row);
            _templateRows = _templateRows.Where(r => r != row).ToList();
            TemplateList.ItemsSource = _templateRows;
            TemplateList.SelectedIndex = Math.Min(i, _templateRows.Count - 1);
            _templateCalendar.Rebuild();
        };
        TemplateNameBox.TextChanged += (_, _) => Edit<PlaylistTemplate>(TemplateList, t => t.Name = TemplateNameBox.Text?.Trim() ?? "");
        TemplateEnabledBox.IsCheckedChanged += (_, _) => Edit<PlaylistTemplate>(TemplateList, t => t.Enabled = TemplateEnabledBox.IsChecked == true);

        AddCategoryLineButton.Click += async (_, _) =>
        {
            if (LineCategoryBox.SelectedIndex is not (>= 0 and var i) || i >= _categories.Count) return;
            var c = _categories[i];
            string name = CategoryTree.PathOf(_categories, c.Id);
            if (await TakeOr(new TemplateLine { Source = LineSource.Category, CategoryId = c.Id, CategoryName = name, Kind = c.Kind }, name) is { } line)
                AddLine(line);
        };
        AddFolderLineButton.Click += async (_, _) =>
        {
            if (SelectedTemplate == null || await PickFolder(L.T("Folder for this line")) is not { } folder) return;
            if (await TakeOr(new TemplateLine { Source = LineSource.Folder, Folder = folder }, Path.GetFileName(folder.TrimEnd('/', '\\'))) is { } line)
                AddLine(line);
        };
        AddTakeLineButton.Click += async (_, _) =>
        {
            if (SelectedTemplate != null && await AskTake(L.T("What should this line say when it goes on air?"), null) is { } kind)
                AddLine(TakeLine(kind));
        };
        // A line of takes picked at random, or a take line: what it says.
        LineTakeButton.Click += async (_, _) =>
        {
            if (SelectedTemplate is not { } t || SelectedLine is not { } l
                || await AskTake(L.T("What should this line say when it goes on air?"), null) is not { } kind) return;
            int i = t.Lines.IndexOf(l);
            Edit<PlaylistTemplate>(TemplateList, _ => t.Lines[i] = TakeLine(kind));
            ShowLines(i);
        };
        LineList.SelectionChanged += (_, _) => ShowLineRules();
        foreach (var box in new[] { TempoMinBox, TempoMaxBox })
            box.ItemsSource = new[] { L.T("any") }.Concat(TempoNames).ToList();
        foreach (var box in new[] { EnergyMinBox, EnergyMaxBox })
            box.ItemsSource = new[] { L.T("any") }.Concat(EnergyNames).ToList();
        void LineRule(ComboBox box, Action<TemplateLine, int?> set) => box.SelectionChanged += (_, _) =>
        {
            if (_loading || SelectedLine is not { } line || LineList.SelectedItem is not ScheduleRow row) return;
            Edit<PlaylistTemplate>(TemplateList, _ => set(line, box.SelectedIndex > 0 ? box.SelectedIndex : null));
            row.Refresh();
        };
        LineRule(TempoMinBox, (l, v) => l.TempoMin = v);
        LineRule(TempoMaxBox, (l, v) => l.TempoMax = v);
        LineRule(EnergyMinBox, (l, v) => l.EnergyMin = v);
        LineRule(EnergyMaxBox, (l, v) => l.EnergyMax = v);
        ArtistHoursBox.ValueChanged += (_, _) => Edit<PlaylistTemplate>(TemplateList, t => t.ArtistHours = (double)(ArtistHoursBox.Value ?? 0));
        AlbumHoursBox.ValueChanged += (_, _) => Edit<PlaylistTemplate>(TemplateList, t => t.AlbumHours = (double)(AlbumHoursBox.Value ?? 0));
        MaxSlowBox.ValueChanged += (_, _) => Edit<PlaylistTemplate>(TemplateList, t => t.MaxSlowInRow = (int)(MaxSlowBox.Value ?? 0));
        LineUpButton.Click += (_, _) => MoveLine(-1);
        LineDownButton.Click += (_, _) => MoveLine(1);
        LineCopyButton.Click += (_, _) =>
        {
            if (SelectedLine is not { } l) return;
            AddLine(new TemplateLine
            {
                Source = l.Source, CategoryId = l.CategoryId, CategoryName = l.CategoryName, Kind = l.Kind, Folder = l.Folder,
                TempoMin = l.TempoMin, TempoMax = l.TempoMax, EnergyMin = l.EnergyMin, EnergyMax = l.EnergyMax,
            });
        };
        RemoveLineButton.Click += (_, _) =>
        {
            if (SelectedTemplate is not { } t || SelectedLine is not { } l) return;
            int i = t.Lines.IndexOf(l);
            Edit<PlaylistTemplate>(TemplateList, _ => t.Lines.Remove(l));
            ShowLines(Math.Min(i, t.Lines.Count - 1));
        };
    }

    static TemplateLine TakeLine(TakeKind kind) => new() { Source = LineSource.Take, Folder = Takes.PathFor(kind), Kind = "jingle" };

    /// <summary>The time, the temperature or both (null: cancelled, or <paramref name="keep"/>: -1 as a kind).</summary>
    async Task<TakeKind?> AskTake(string message, string? keep)
    {
        var choices = new List<string> { L.T("🕒  The time (the hour and minutes)"), L.T("🌡  The temperature"), L.T("🕒 🌡  The time and then the temperature") };
        if (keep != null) choices.Add(keep);
        return await Dialogs.Choose(this, L.T("Time / temperature"), message, choices) switch
        {
            0 => TakeKind.Time,
            1 => TakeKind.Temperature,
            2 => TakeKind.TimeAndTemperature,
            3 => (TakeKind)(-1),
            _ => null,
        };
    }

    /// <summary>
    /// A category or folder line, or — when its files are takes (TMP18, HRS10…) — a take line, asked: what it says.
    /// Null when cancelled.
    /// </summary>
    async Task<TemplateLine?> TakeOr(TemplateLine line, string name)
    {
        if (!PlaysTakesAtRandom(line)) return line;
        return await AskTake(L.F("{0} has takes (TMP18, HRS10, MIN15…). What should this line say when it goes on air?", name),
                L.T("Any one of its files, at random")) switch
        {
            null => null,
            (TakeKind)(-1) => line,
            { } kind => TakeLine(kind),
        };
    }

    /// <summary>Puts a line after the selected one (or at the end).</summary>
    void AddLine(TemplateLine line)
    {
        if (SelectedTemplate is not { } t) return;
        int at = SelectedLine is { } sel ? t.Lines.IndexOf(sel) + 1 : t.Lines.Count;
        Edit<PlaylistTemplate>(TemplateList, _ => t.Lines.Insert(at, line));
        ShowLines(at);
    }

    void MoveLine(int delta)
    {
        if (SelectedTemplate is not { } t || SelectedLine is not { } l) return;
        int i = t.Lines.IndexOf(l), j = i + delta;
        if (j < 0 || j >= t.Lines.Count) return;
        Edit<PlaylistTemplate>(TemplateList, _ => (t.Lines[i], t.Lines[j]) = (t.Lines[j], t.Lines[i]));
        ShowLines(j);
    }

    void ShowTemplate()
    {
        var t = SelectedTemplate;
        TemplatePanel.IsEnabled = t != null;
        _loading = true;
        TemplateNameBox.Text = t?.Name ?? "";
        TemplateEnabledBox.IsChecked = t?.Enabled ?? true;
        ArtistHoursBox.Value = (decimal)(t?.ArtistHours ?? 0);
        AlbumHoursBox.Value = (decimal)(t?.AlbumHours ?? 0);
        MaxSlowBox.Value = t?.MaxSlowInRow ?? 0;
        _loading = false;
        ShowLines(0);
        _templateCalendar.Rebuild();
    }

    void ShowLines(int select)
    {
        var t = SelectedTemplate;
        LineList.ItemsSource = t?.Lines.Select((l, i) => LineRow(l, i)).ToList();
        LineList.SelectedIndex = t == null || t.Lines.Count == 0 ? -1 : Math.Clamp(select, 0, t.Lines.Count - 1);
    }

    ScheduleRow LineRow(TemplateLine l, int index) => new(l, () =>
    {
        var (title, detail) = l.Source == LineSource.Take
            ? ($"{index + 1}.  🕒  {TakeName(l.Folder)}", L.T("a take, put together when it goes on air"))
            : l.Source == LineSource.Folder
            ? ($"{index + 1}.  📁  {Path.GetFileName(l.Folder.TrimEnd('/', '\\'))}", L.F("a file from the folder {0}", l.Folder) + (SharedPaths.DirectoryExists(l.Folder) ? "" : L.T(" · not found")))
            : ($"{index + 1}.  {KindIcon(l.Kind)}  {CategoryLabel(l)}", FromCategory(l.Kind) + RulesText(l));
        return (title, PlaysTakesAtRandom(l) ? detail + "  " + TakesWarning : detail, true);
    });

    static string TakesWarning => L.T("⚠ These are takes: it plays one at random (a temperature or time at random). For the time or temperature now: select it → 🕒 Time / temperature…");

    readonly Dictionary<string, bool> _takeLines = new();

    /// <summary>A category or folder line whose files are takes (TMP18, HRS10…): it would play one at random.</summary>
    internal bool PlaysTakesAtRandom(TemplateLine l)
    {
        if (l.Source == LineSource.Take) return false;
        string key = l.Source == LineSource.Folder ? "f:" + l.Folder : "c:" + l.CategoryId;
        if (_takeLines.TryGetValue(key, out bool known)) return known;
        bool takes = false;
        try
        {
            var paths = l.Source == LineSource.Folder
                ? (SharedPaths.DirectoryExists(l.Folder) ? Directory.EnumerateFiles(SharedPaths.ToLocal(l.Folder)).Take(60).ToList() : [])
                : _library?.Db is { } db ? db.Search("", new CategoryFilter(l.CategoryId), 60).Tracks.Select(t => t.Path).ToList() : null;
            if (paths == null) return false; // the library isn't open yet: looked at again later
            takes = Takes.AreTakeFiles(paths);
        }
        catch { /* can't tell */ }
        return _takeLines[key] = takes;
    }

    /// <summary>What a take line says, for the lists.</summary>
    internal static string TakeName(string take) => Takes.KindOf(take) switch
    {
        TakeKind.Time => L.T("Take: the time"),
        TakeKind.Temperature => L.T("Take: the temperature"),
        _ => L.T("Take: the time and temperature"),
    };

    static string[] TempoNames => [L.T("1 · Slow"), "2", L.T("3 · Medium"), "4", L.T("5 · Fast")];
    static string[] EnergyNames => [L.T("1 · Calm"), "2", "3", "4", L.T("5 · Lively")];

    /// <summary>" · tempo 4–5 · energy 3+" for a line that asks for them.</summary>
    static string RulesText(TemplateLine l)
    {
        static string Range(int? min, int? max) => (min, max) switch
        {
            ({ } a, { } b) when a == b => $"{a}",
            ({ } a, { } b) => $"{a}–{b}",
            ({ } a, null) => $"{a}+",
            (null, { } b) => L.F("up to {0}", b),
            _ => "",
        };
        string text = "";
        if (l.TempoMin != null || l.TempoMax != null) text += " · " + L.F("tempo {0}", Range(l.TempoMin, l.TempoMax));
        if (l.EnergyMin != null || l.EnergyMax != null) text += " · " + L.F("energy {0}", Range(l.EnergyMin, l.EnergyMax));
        return text;
    }

    /// <summary>The selected line's tempo and energy (library lines of songs only).</summary>
    void ShowLineRules()
    {
        var l = SelectedLine;
        LineRulesPanel.IsVisible = l is { Source: LineSource.Category } && l.TrackKind == TrackKind.Music;
        LineTakeButton.IsVisible = l != null && (l.Source == LineSource.Take || PlaysTakesAtRandom(l));
        _loading = true;
        TempoMinBox.SelectedIndex = l?.TempoMin ?? 0;
        TempoMaxBox.SelectedIndex = l?.TempoMax ?? 0;
        EnergyMinBox.SelectedIndex = l?.EnergyMin ?? 0;
        EnergyMaxBox.SelectedIndex = l?.EnergyMax ?? 0;
        _loading = false;
    }

    string CategoryLabel(TemplateLine l) =>
        _categories.Any(c => c.Id == l.CategoryId) ? CategoryTree.PathOf(_categories, l.CategoryId) : l.CategoryName;

    static string KindIcon(string kind) => kind switch { "jingle" => "✦", "commercial" => "€", "other" => "◆", _ => "♫" };

    static string FromCategory(string kind) => kind switch
    {
        "jingle" => L.T("a jingle from the library category"),
        "commercial" => L.T("an advert from the library category"),
        "other" => L.T("an item from the library category"),
        _ => L.T("a song from the library category"),
    };

    ScheduleRow TemplateRow(PlaylistTemplate t) => new(t, () =>
        (TemplateName(t), L.F("{0} · {1}", L.F(t.Lines.Count == 1 ? "{0} line" : "{0} lines", t.Lines.Count), HoursSummary(t)), t.Enabled));

    static string TemplateName(PlaylistTemplate t) => string.IsNullOrWhiteSpace(t.Name) ? L.T("Playlist") : t.Name.Trim();

    /// <summary>"07–10 · Mon–Fri" for one block of hours on the same days; else how many hours a week.</summary>
    static string HoursSummary(PlaylistTemplate t)
    {
        if (t.Slots.Count == 0) return L.T("not in the calendar yet");
        var byHour = t.Slots.GroupBy(s => s.At.Hours).OrderBy(g => g.Key).ToList();
        var days = byHour[0].Select(s => s.Day).ToHashSet();
        bool sameDays = byHour.All(g => g.Select(s => s.Day).ToHashSet().SetEquals(days));
        bool block = byHour[^1].Key - byHour[0].Key == byHour.Count - 1;
        if (!sameDays || !block) return L.F("{0} hours a week", t.Slots.Count);
        string hours = byHour.Count == 1 ? $"{byHour[0].Key:00}:00" : $"{byHour[0].Key:00}–{byHour[^1].Key + 1:00}";
        return $"{hours} · {DaysText(days)}";
    }

    /// <summary>The music library's categories, for the lines (read in the background; the library may be on a server).</summary>
    async Task LoadCategories()
    {
        LibraryNote.Text = L.T("Reading the music library…");
        AddCategoryLineButton.IsEnabled = false;
        try
        {
            var settings = AppSettings.Load();
            var library = new LibraryService();
            await library.OpenAsync(settings.Library);
            if (library.Db is not { } db)
            {
                LibraryNote.Text = L.F("The music library isn't available ({0}); folders can still be used.", library.Error ?? L.T("not set up"));
                return;
            }
            var (categories, counts) = await Task.Run(() => (db.Categories().ToList(), db.CategoryCounts()));
            // In tree order; a category's line also picks from the ones under it.
            var tree = CategoryTree.Flatten(categories);
            _categories = tree.Select(x => x.Category).ToList();
            LineCategoryBox.ItemsSource = tree.Select(x =>
            {
                int songs = CategoryTree.WithDescendants(categories, [x.Category.Id]).Sum(id => counts.GetValueOrDefault(id));
                return $"{new string(' ', 4 * x.Depth)}{KindIcon(x.Category.Kind)}  {x.Category.Name}  ({songs})";
            }).ToList();
            AddCategoryLineButton.IsEnabled = categories.Count > 0;
            LibraryNote.Text = categories.Count == 0 ? L.T("The music library has no categories yet; folders can still be used.") : "";
            ShowLines(LineList.SelectedIndex);
        }
        catch (Exception ex)
        {
            LibraryNote.Text = L.F("The music library isn't available ({0}); folders can still be used.", ex.Message);
        }
    }

    // ───────────────────────── Ad breaks ─────────────────────────

    void WireBreaks()
    {
        BreakList.SelectionChanged += (_, _) => ShowBreak();
        AddBreakButton.Click += (_, _) =>
        {
            var b = new AdBreak { Name = L.T("Ad break") };
            b.Normalize(); // a section to book adverts into
            _data.Breaks.Add(b);
            _breakRows = _breakRows.Append(BreakRow(b)).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = _breakRows.Count - 1;
            SectionsChanged();
            BreakNameBox.Focus();
            BreakNameBox.SelectAll();
        };
        DeleteBreakButton.Click += async (_, _) =>
        {
            if (BreakList.SelectedItem is not ScheduleRow { Item: AdBreak b } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete ad break"), L.F("Delete \"{0}\"?", EntryName(b)), L.T("Delete"))) return;
            _data.Breaks.Remove(b);
            int i = _breakRows.IndexOf(row);
            _breakRows = _breakRows.Where(r => r != row).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = Math.Min(i, _breakRows.Count - 1);
            _breakCalendar.Rebuild();
            SectionsChanged();
            ShowSpotWeek();
        };
        BreakNameBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Name = BreakNameBox.Text?.Trim() ?? "");
        BreakEnabledBox.IsCheckedChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Enabled = BreakEnabledBox.IsChecked == true);
        WireStart(BreakList, BreakSoftBox, BreakHardBox, BreakEndBox, BreakDelayBox, BreakLockBox, BreakDelayPanel);

        // The running order.
        PartList.SelectionChanged += (_, _) => ShowPart();
        AddJingleButton.Click += async (_, _) =>
        {
            if (SelectedBreak == null || await PickFile(L.T("Jingle")) is not { } path) return;
            AddPart(new BreakPart { Kind = PartKind.Jingle, Path = path });
        };
        AddCategoryPartButton.Click += (_, _) => ChooseCategoryPart();
        AddSectionButton.Click += (_, _) =>
        {
            if (SelectedBreak is not { } b) return;
            string name = SectionNameBox.Text?.Trim() ?? "";
            if (name.Length == 0)
            {
                ErrorText.Text = L.T("Type the section's name, e.g. Before the hour.");
                return;
            }
            if (b.SectionNames.Any(n => n.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
            {
                ErrorText.Text = L.F("This break already has the section \"{0}\".", name);
                return;
            }
            // The same name as in other breaks, as it was written there.
            name = _data.Sections().FirstOrDefault(n => n.Equals(name, StringComparison.CurrentCultureIgnoreCase)) ?? name;
            AddPart(new BreakPart { Kind = PartKind.Section, Section = name });
            SectionNameBox.Text = "";
        };
        PartUpButton.Click += (_, _) => MovePart(-1);
        PartDownButton.Click += (_, _) => MovePart(1);
        RemovePartButton.Click += (_, _) =>
        {
            if (SelectedBreak is not { } b || SelectedPart is not { } part) return;
            int i = b.Parts.IndexOf(part);
            b.Parts.Remove(part);
            PartsChanged(Math.Min(i, b.Parts.Count - 1));
        };
        PartOnlyBox.IsCheckedChanged += (_, _) =>
        {
            if (_loading || SelectedPart is not { Kind: PartKind.Jingle } part) return;
            part.OnlyWithAdverts = PartOnlyBox.IsChecked == true;
            (PartList.SelectedItem as ScheduleRow)?.Refresh();
        };
    }

    AdBreak? SelectedBreak => (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak;
    BreakPart? SelectedPart => (PartList.SelectedItem as ScheduleRow)?.Item as BreakPart;

    /// <summary>
    /// From a category…: the library's categories but Music (and Takes: the time and temperature have their own
    /// lines), to take a file from each time the break goes on air.
    /// </summary>
    void ChooseCategoryPart()
    {
        if (SelectedBreak == null) return;
        var left = new HashSet<long>();
        foreach (var root in _categories.Where(c => c.Role is "music" or "takes"))
            left.UnionWith(CategoryTree.WithDescendants(_categories, [root.Id]));
        var choices = CategoryTree.Flatten(_categories).Where(x => !left.Contains(x.Category.Id)).ToList();
        if (choices.Count == 0)
        {
            ErrorText.Text = L.T("The music library isn't available, or has no categories but Music.");
            return;
        }
        var menu = new MenuFlyout();
        foreach (var (category, depth) in choices)
        {
            var item = new MenuItem { Header = $"{new string(' ', 4 * depth)}{KindIcon(category.Kind)}  {category.Name}" };
            long id = category.Id;
            item.Click += (_, _) => AddPart(BreakPart.FromCategory(id, CategoryTree.PathOf(_categories, id)));
            menu.Items.Add(item);
        }
        Avalonia.Controls.Primitives.FlyoutBase.SetAttachedFlyout(AddCategoryPartButton, menu);
        menu.ShowAt(AddCategoryPartButton);
    }

    /// <summary>Puts a part after the selected one (or at the end).</summary>
    void AddPart(BreakPart part)
    {
        if (SelectedBreak is not { } b) return;
        int at = SelectedPart is { } sel ? b.Parts.IndexOf(sel) + 1 : b.Parts.Count;
        b.Parts.Insert(at, part);
        PartsChanged(at);
    }

    void MovePart(int delta)
    {
        if (SelectedBreak is not { } b || SelectedPart is not { } part) return;
        int i = b.Parts.IndexOf(part), j = i + delta;
        if (j < 0 || j >= b.Parts.Count) return;
        (b.Parts[i], b.Parts[j]) = (b.Parts[j], b.Parts[i]);
        PartsChanged(j);
    }

    void PartsChanged(int select)
    {
        ShowParts(select);
        ErrorText.Text = "";
        (BreakList.SelectedItem as ScheduleRow)?.Refresh();
        SectionsChanged();
        ShowBooked();
        SpotGrid.Refresh();
    }

    void ShowParts(int select)
    {
        var b = SelectedBreak;
        PartList.ItemsSource = b?.Parts.Select(p => PartRow(b, p)).ToList();
        PartList.SelectedIndex = b == null ? -1 : Math.Clamp(select, -1, b.Parts.Count - 1);
        ShowPart();
    }

    void ShowPart()
    {
        var part = SelectedPart;
        _loading = true;
        PartOnlyBox.IsEnabled = part?.Kind == PartKind.Jingle;
        PartOnlyBox.IsChecked = part?.OnlyWithAdverts == true;
        _loading = false;
    }

    ScheduleRow PartRow(AdBreak b, BreakPart p) => new(p, () => p.CategoryId is { } category
        ? ("♪  " + L.F("From {0}", _categories.Any(c => c.Id == category) ? CategoryTree.PathOf(_categories, category) : p.Section),
            (p.OnlyWithAdverts ? L.T("a file from the category · only when the section after it has adverts") : L.T("a file from the category, a different one each time"))
            + (_categories.Count > 0 && _categories.All(c => c.Id != category) ? L.T(" · the category isn't there any more") : ""), true)
        : p.Kind == PartKind.Jingle
        ? ("♪  " + (Path.GetFileNameWithoutExtension(p.Path) is { Length: > 0 } n ? n : L.T("Jingle")),
            (p.OnlyWithAdverts ? L.T("jingle · only when the section after it has adverts") : L.T("jingle")) + (SharedPaths.FileExists(p.Path) ? "" : L.T(" · not found")), true)
        : ("€  " + p.Section, L.F("advert section · {0} today", Plural(b.TimesOn(DateTime.Today).Sum(t => _data.InSection(b, p.Section, t).Count), "{0} advert", "{0} adverts")), true));

    /// <summary>The section names offered when adding a section and booking a campaign, after they changed.</summary>
    void SectionsChanged()
    {
        SectionNameBox.ItemsSource = _data.Sections();
        ShowCampaign();
        RefreshRows(_campaignRows);
    }

    void ShowBreak()
    {
        var b = SelectedBreak;
        BreakPanel.IsEnabled = b != null;
        _loading = true;
        BreakNameBox.Text = b?.Name ?? "";
        BreakEnabledBox.IsChecked = b?.Enabled ?? true;
        ShowStart(b, BreakSoftBox, BreakHardBox, BreakEndBox, BreakDelayBox, BreakLockBox, BreakDelayPanel);
        _loading = false;
        ShowParts(0);
        ShowBooked();
        _breakCalendar.Rebuild();
    }

    void ShowBooked()
    {
        var b = SelectedBreak;
        BreakBookedText.Text = b == null ? "" : L.F("Today: {0}", Plural(BookedToday(b), "{0} advert", "{0} adverts"));
        RefreshRows(PartList.ItemsSource as IEnumerable<ScheduleRow> ?? []);
    }

    /// <summary>How many adverts the break plays today, at all its times.</summary>
    int BookedToday(AdBreak b) => b.TimesOn(DateTime.Today).Sum(t => _data.InBreak(b, t).Count);

    ScheduleRow BreakRow(AdBreak b) => new(b, () =>
        (EntryName(b), L.F("{0} · {1} · {2} today", SlotsSummary(b), StartText(b), Plural(BookedToday(b), "{0} advert", "{0} adverts")), b.Enabled));

    // ───────────────────────── Clients ─────────────────────────

    AdClient? SelectedClient => (ClientList.SelectedItem as ScheduleRow)?.Item as AdClient;
    AdCampaign? SelectedCampaign => (CampaignList.SelectedItem as ScheduleRow)?.Item as AdCampaign;

    void WireClients()
    {
        ClientList.SelectionChanged += (_, _) => ShowClient();
        CampaignList.SelectionChanged += (_, _) =>
        {
            ShowCampaign();
            SpotGrid.Refresh();
        };
        CertificateButton.Click += async (_, _) =>
        {
            if (SelectedClient is not { } c) return;
            string station = _station?.StationName is { Length: > 0 } n ? n : _settings?.StationName ?? "";
            await new CertificateWindow(c, _library?.Db, _settings, station).ShowDialog(this);
        };
        AddClientButton.Click += (_, _) =>
        {
            var c = new AdClient { Name = L.T("New client") };
            _data.Clients.Add(c);
            _clientRows = _clientRows.Append(ClientRow(c)).ToList();
            ClientList.ItemsSource = _clientRows;
            ClientList.SelectedIndex = _clientRows.Count - 1;
            ClientNameBox.Focus();
            ClientNameBox.SelectAll();
        };
        DeleteClientButton.Click += async (_, _) =>
        {
            if (ClientList.SelectedItem is not ScheduleRow { Item: AdClient c } row) return;
            string more = c.Campaigns.Count > 0 ? L.F(" Its {0} go too.", Plural(c.Campaigns.Count, "{0} campaign", "{0} campaigns")) : "";
            if (!await Dialogs.Confirm(this, L.T("Delete client"), L.F("Delete \"{0}\"?{1} The files stay on disk.", ClientName(c), more), L.T("Delete"))) return;
            _data.Clients.Remove(c);
            int i = _clientRows.IndexOf(row);
            _clientRows = _clientRows.Where(r => r != row).ToList();
            ClientList.ItemsSource = _clientRows;
            ClientList.SelectedIndex = Math.Min(i, _clientRows.Count - 1);
            AdvertsChanged();
        };
        AddCampaignButton.Click += async (_, _) =>
        {
            if (SelectedClient is not { } c || await PickFile(L.T("Advert")) is not { } path) return;
            var k = new AdCampaign { Name = Path.GetFileNameWithoutExtension(path), Files = [path], From = DateTime.Today };
            c.Campaigns.Add(k);
            _campaignRows = _campaignRows.Append(CampaignRow(k)).ToList();
            CampaignList.ItemsSource = _campaignRows;
            CampaignList.SelectedIndex = _campaignRows.Count - 1;
            (ClientList.SelectedItem as ScheduleRow)?.Refresh();
            CampaignNameBox.Focus();
            CampaignNameBox.SelectAll();
        };
        DeleteCampaignButton.Click += async (_, _) =>
        {
            if (SelectedClient is not { } c || CampaignList.SelectedItem is not ScheduleRow { Item: AdCampaign k } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete campaign"), L.F("Delete \"{0}\"? The files stay on disk.", CampaignName(k)), L.T("Delete"))) return;
            c.Campaigns.Remove(k);
            int i = _campaignRows.IndexOf(row);
            _campaignRows = _campaignRows.Where(r => r != row).ToList();
            CampaignList.ItemsSource = _campaignRows;
            CampaignList.SelectedIndex = Math.Min(i, _campaignRows.Count - 1);
            (ClientList.SelectedItem as ScheduleRow)?.Refresh();
            AdvertsChanged();
        };

        ClientNameBox.TextChanged += (_, _) => Edit<AdClient>(ClientList, c => c.Name = ClientNameBox.Text?.Trim() ?? "");
        ClientEnabledBox.IsCheckedChanged += (_, _) => Edit<AdClient>(ClientList, c => c.Enabled = ClientEnabledBox.IsChecked == true);
        CampaignNameBox.TextChanged += (_, _) => Edit<AdCampaign>(CampaignList, k => k.Name = CampaignNameBox.Text?.Trim() ?? "");
        CampaignEnabledBox.IsCheckedChanged += (_, _) => Edit<AdCampaign>(CampaignList, k => k.Enabled = CampaignEnabledBox.IsChecked == true);
        AddFileButton.Click += async (_, _) =>
        {
            if (SelectedCampaign == null || await PickFile(L.T("Advert")) is not { } path) return;
            Edit<AdCampaign>(CampaignList, k => k.Files.Add(path));
            ShowFiles();
        };
        RemoveFileButton.Click += (_, _) =>
        {
            if (CampaignFiles.SelectedIndex is not (>= 0 and var i)) return;
            Edit<AdCampaign>(CampaignList, k => k.Files.RemoveAt(i));
            ShowFiles();
        };
        CampaignPositionBox.ItemsSource = Enumerable.Range(0, 11).Select(n => n switch
        {
            0 => L.T("Automatic"),
            1 => L.T("1 (first)"),
            10 => L.T("10 (last)"),
            _ => n.ToString(CultureInfo.CurrentCulture),
        }).ToList();
        CampaignPositionBox.SelectionChanged += (_, _) => Edit<AdCampaign>(CampaignList, k => k.Position = Math.Max(0, CampaignPositionBox.SelectedIndex));
        CampaignSectionBox.SelectionChanged += (_, _) =>
        {
            if (CampaignSectionBox.SelectedIndex < 0) return;
            Edit<AdCampaign>(CampaignList, k => k.Section = CampaignSectionBox.SelectedIndex == 0 ? "" : CampaignSectionBox.SelectedItem as string ?? "");
            ShowSectionShown();
        };
        SpotFromBox.SelectedDateChanged += (_, _) => Edit<AdCampaign>(CampaignList, k => k.From = SpotFromBox.SelectedDate?.Date);
        SpotToBox.SelectedDateChanged += (_, _) => Edit<AdCampaign>(CampaignList, k => k.To = SpotToBox.SelectedDate?.Date);
        SpotClearDatesButton.Click += (_, _) =>
        {
            SpotFromBox.SelectedDate = null;
            SpotToBox.SelectedDate = null;
        };
        SpotClearPlacesButton.Click += (_, _) => Edit<AdCampaign>(CampaignList, k => k.Placements.Clear());
        SpotEveryBreakButton.Click += (_, _) => Edit<AdCampaign>(CampaignList, k =>
        {
            // Every week, at every day and time of a break that has its section (places on a date stay).
            k.Placements.RemoveAll(p => p.Date == null);
            foreach (var b in _data.Breaks.Where(b => b.Enabled && b.SectionOf(k) != null))
                foreach (var slot in b.Slots)
                    if (!k.Placements.Any(p => p.Date == null && p.Day == slot.Day && p.At == slot.At))
                        k.Placements.Add(new AdPlacement { Day = slot.Day, Time = slot.Time });
        });
        SpotOrphanButton.Click += (_, _) => Edit<AdCampaign>(CampaignList, k => k.Placements.RemoveAll(p => !HasBreak(k, p)));
        SpotMoveOrphansButton.Click += (_, _) => Edit<AdCampaign>(CampaignList, k => MoveToBreaks(k));

        // The week calendar of breaks.
        SpotGrid.CellOf = SpotCell;
        SpotGrid.PaintStarted += () => _spotSnapshot = SelectedCampaign?.Placements.Select(Copy).ToList();
        SpotGrid.Paint += (cells, on) =>
        {
            if (SelectedCampaign is not { } k || _spotSnapshot == null) return;
            k.Placements = _spotSnapshot.Select(Copy).ToList();
            foreach (var (col, row) in cells)
            {
                var date = _week.AddDays(col);
                var time = TimeSpan.FromSeconds(_spotTimes[row]);
                if (!BreaksAt(date + time).Any(b => b.SectionOf(k) != null)) continue; // no break there with its section
                k.Placements.RemoveAll(p => p.At == time && (p.Date is { } d ? d.Date == date : p.Day == date.DayOfWeek));
                if (on)
                    k.Placements.Add(ThatDayBox.IsChecked == true
                        ? new AdPlacement { Date = date, Time = WeekTime.Text(time) }
                        : new AdPlacement { Day = date.DayOfWeek, Time = WeekTime.Text(time) });
            }
        };
        SpotGrid.PaintEnded += () => Edit<AdCampaign>(CampaignList, _ => { });
        WeekBackButton.Click += (_, _) => { _week = _week.AddDays(-7); ShowSpotWeek(); };
        WeekNextButton.Click += (_, _) => { _week = _week.AddDays(7); ShowSpotWeek(); };
        ThisWeekButton.Click += (_, _) => { _week = Monday(DateTime.Today); ShowSpotWeek(); };
    }

    static AdPlacement Copy(AdPlacement p) => new() { Day = p.Day, Date = p.Date, Time = p.Time };

    void ShowClient()
    {
        var c = SelectedClient;
        ClientPanel.IsEnabled = c != null;
        AddCampaignButton.IsEnabled = DeleteCampaignButton.IsEnabled = c != null;
        _loading = true;
        ClientNameBox.Text = c?.Name ?? "";
        ClientEnabledBox.IsChecked = c?.Enabled ?? true;
        _loading = false;
        _campaignRows = c?.Campaigns.Select(CampaignRow).ToList() ?? new();
        CampaignList.ItemsSource = _campaignRows;
        CampaignList.SelectedIndex = _campaignRows.Count > 0 ? 0 : -1;
        ShowCampaign();
        SpotGrid.Refresh();
    }

    void ShowCampaign()
    {
        var k = SelectedCampaign;
        CampaignPanel.IsEnabled = k != null;
        _loading = true;
        CampaignNameBox.Text = k?.Name ?? "";
        CampaignEnabledBox.IsChecked = k?.Enabled ?? true;
        var sections = _data.Sections();
        if (k is { Section.Length: > 0 } && !sections.Contains(k.Section, StringComparer.CurrentCultureIgnoreCase)) sections.Add(k.Section);
        CampaignSectionBox.ItemsSource = sections.Prepend(L.T("The break's first section")).ToList();
        CampaignSectionBox.SelectedIndex = k == null || k.Section.Length == 0 ? 0
            : sections.FindIndex(n => n.Equals(k.Section, StringComparison.CurrentCultureIgnoreCase)) + 1;
        CampaignPositionBox.SelectedIndex = Math.Clamp(k?.Position ?? 0, 0, 10);
        SpotFromBox.SelectedDate = k?.From;
        SpotToBox.SelectedDate = k?.To;
        _loading = false;
        ShowFiles();
        ShowPlaced();
        ShowSectionShown();
    }

    void ShowFiles() => CampaignFiles.ItemsSource = SelectedCampaign?.Files.Select(f => Path.GetFileName(f) + (SharedPaths.FileExists(f) ? "" : L.T(" · not found"))).ToList();

    void ShowSectionShown() =>
        SectionShownText.Text = SelectedCampaign is { } k
            ? L.F("Showing the section: {0}", k.Section.Length > 0 ? k.Section : L.T("the break's first section"))
            : L.T("Showing every section: choose a campaign to place it");

    void ShowPlaced()
    {
        var k = SelectedCampaign;
        int places = k?.Placements.Count ?? 0;
        SpotPlacedText.Text = k == null ? "" : places == 0
            ? L.T("Not placed in the calendar yet: click the breaks it goes in.")
            : L.F("Placed in {0} of the calendar ({1} every week, {2} on a date).", Plural(places, "{0} break", "{0} breaks"), k.Placements.Count(p => p.Date == null), k.Placements.Count(p => p.Date != null));
        SpotClearPlacesButton.IsVisible = places > 0;
        // Places where there is no break (any more): a break moved to another time leaves them behind.
        var orphans = k?.Placements.Where(p => !HasBreak(k, p)).ToList() ?? new();
        SpotOrphanText.Text = orphans.Count == 0 ? ""
            : L.F("{0} with no break that has its section (e.g. {1}): it doesn't play there. A break moved to another time? Place it again.",
                Plural(orphans.Count, "{0} place", "{0} places"),
                string.Join(", ", orphans.Select(p => p.Date is { } d ? d.ToString("d/M ", CultureInfo.CurrentCulture) + WeekGrid.Time(p.At.TotalSeconds) : DayShort(p.Day ?? DayOfWeek.Monday) + " " + WeekGrid.Time(p.At.TotalSeconds)).Distinct().Take(3)));
        SpotOrphanButton.IsVisible = SpotMoveOrphansButton.IsVisible = orphans.Count > 0;
        SpotEveryBreakButton.IsVisible = k != null;
    }

    /// <summary>
    /// A campaign's places with no break (a break moved to another time) each to the break nearest it on the same day
    /// that has its section (10:50 → 11:00); two that land on the same break are one.
    /// </summary>
    void MoveToBreaks(AdCampaign k)
    {
        var breaks = _data.Breaks.Where(b => b.Enabled && b.SectionOf(k) != null).ToList();
        foreach (var p in k.Placements.Where(p => !HasBreak(k, p)).ToList())
        {
            var times = p.Date is { } d
                ? breaks.SelectMany(b => b.TimesOn(d.Date)).Select(t => t.TimeOfDay)
                : breaks.SelectMany(b => b.Slots.Where(s => s.Day == p.Day)).Select(s => s.At);
            if (times.OrderBy(t => (t - p.At).Duration()).ThenByDescending(t => t).Cast<TimeSpan?>().FirstOrDefault() is { } nearest)
                p.Time = WeekTime.Text(nearest);
        }
        k.Placements = k.Placements.GroupBy(p => (p.Day, p.Date?.Date, p.Time)).Select(g => g.First()).ToList();
    }

    /// <summary>The campaigns with places where there is no break: they don't play there (for Home).</summary>
    internal List<(AdClient Client, AdCampaign Campaign, int Places)> Unplaced() =>
        _data.Clients.Where(c => c.Enabled).SelectMany(c => c.Campaigns.Where(k => k.Enabled && (k.To == null || k.To >= DateTime.Today))
            .Select(k => (c, k, k.Placements.Count(p => !HasBreak(k, p))))).Where(x => x.Item3 > 0).ToList();

    /// <summary>Whether there is a break at a place of the campaign that has its section.</summary>
    bool HasBreak(AdCampaign k, AdPlacement p) => _data.Breaks.Any(b => b.Enabled && b.SectionOf(k) != null
        && (p.Date is { } d ? b.TimesOn(d.Date).Contains(d.Date + p.At) : b.Slots.Any(s => s.Day == p.Day && s.At == p.At)));

    /// <summary>After clients or campaigns changed: the counts in the breaks and the calendar.</summary>
    void AdvertsChanged()
    {
        RefreshRows(_breakRows);
        ShowBooked();
        ShowCampaign();
        SpotGrid.Refresh();
    }

    /// <summary>The adverts calendar for <see cref="_week"/>: a row for every break time of the week.</summary>
    void ShowSpotWeek()
    {
        var days = Enumerable.Range(0, 7).Select(d => _week.AddDays(d)).ToList();
        _spotTimes = _data.Breaks.Where(b => b.Enabled)
            .SelectMany(b => days.SelectMany(d => b.TimesOn(d)).Select(t => t.TimeOfDay.TotalSeconds))
            .Distinct().Order().ToList();
        SpotGrid.Times = _spotTimes;
        SpotNoBreaks.IsVisible = _spotTimes.Count == 0;
        var culture = CultureInfo.CurrentCulture;
        WeekText.Text = L.F("Week {0}: {1} – {2}", ISOWeek.GetWeekOfYear(_week), _week.ToString("d MMM", culture), _week.AddDays(6).ToString("d MMM yyyy", culture));
        Header(SpotHeader, days.Select(d => ($"{DayShort(d.DayOfWeek)} {d.ToString("d/M", culture)}", d == DateTime.Today)).ToList());
        SpotGrid.Refresh();
    }

    List<AdBreak> BreaksAt(DateTime at) => _data.Breaks.Where(b => b.Enabled && b.TimesOn(at.Date).Contains(at)).ToList();

    /// <summary>
    /// A break in the adverts calendar: with a campaign chosen, the adverts in its section (where the break has that
    /// section; elsewhere it can't be clicked), highlighted where the campaign is placed; else all the break's adverts.
    /// </summary>
    GridCell SpotCell(int col, int row)
    {
        var at = _week.AddDays(col) + TimeSpan.FromSeconds(_spotTimes[row]);
        var breaks = BreaksAt(at);
        if (breaks.Count == 0) return new GridCell("", false, Enabled: false);
        var k = SelectedCampaign;
        var adverts = k == null
            ? breaks.SelectMany(b => _data.InBreak(b, at)).ToList()
            : breaks.SelectMany(b => b.SectionOf(k) is { } s ? _data.InSection(b, s, at) : []).ToList();
        bool usable = k == null || breaks.Any(b => b.SectionOf(k) != null);
        if (!usable) return new GridCell("—", false, Enabled: false);
        double seconds = adverts.Sum(a => Length(a.File));
        bool on = k != null && k.Placements.Any(p => p.On(at)) && adverts.Any(a => a.Campaign == k);
        bool placedOutside = k != null && !on && k.Placements.Any(p => p.On(at)); // placed, but outside its dates
        string text = adverts.Count == 0 ? placedOutside ? "·" : "" : $"{adverts.Count:00} | {TimeSpan.FromSeconds(Math.Round(seconds)):m\\:ss}";
        return new GridCell(text, on || placedOutside, k != null, adverts.Count > 0);
    }

    /// <summary>An advert's length (read once from the file).</summary>
    double Length(string path)
    {
        if (_lengths.TryGetValue(path, out double s)) return s;
        s = SharedPaths.FileExists(path) ? AudioProbe.Duration(path) ?? 0 : 0;
        _lengths[path] = s;
        return s;
    }

    ScheduleRow ClientRow(AdClient c) => new(c, () =>
    {
        int running = c.Campaigns.Count(k => k.Enabled && k.Placements.Count > 0 && !(k.To is { } to && to.Date < DateTime.Today));
        return (ClientName(c), L.F("{0} · {1} running", Plural(c.Campaigns.Count, "{0} campaign", "{0} campaigns"), running), c.Enabled);
    });

    ScheduleRow CampaignRow(AdCampaign k) => new(k, () =>
    {
        string dates = (k.From, k.To) switch
        {
            (null, null) => L.T("no end date"),
            ({ } f, null) => L.F("from {0:yyyy-MM-dd}", f),
            (null, { } t) => L.F("until {0:yyyy-MM-dd}", t),
            ({ } f, { } t) => L.F("{0:yyyy-MM-dd} to {1:yyyy-MM-dd}", f, t),
        };
        bool over = k.To is { } to && to.Date < DateTime.Today;
        string where = k.Section.Length > 0 ? k.Section : L.T("first section");
        string position = k.Position == 0 ? "" : L.F(" · position {0}", k.Position);
        return (CampaignName(k), L.F("{0}{1} · {2}{3} · {4}", dates, (over ? L.T(" (ended)") : ""), where, position, Plural(k.Placements.Count, "{0} place", "{0} places")),
            k.Enabled && !over);
    });

    static string ClientName(AdClient c) => c.Name.Length > 0 ? c.Name : L.T("Client");
    static string CampaignName(AdCampaign k) => k.Name.Length > 0 ? k.Name : k.Files.Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? L.T("Campaign");

    // ───────────────────────── The events and breaks calendars ─────────────────────────

    /// <summary>
    /// A week calendar for events or breaks: rows every few minutes (and the times entries already have, and rows
    /// added by hand), a column a day. A click puts the selected entry at that day and time; other entries show faintly.
    /// </summary>
    sealed class EntryCalendar
    {
        readonly WeekGrid _grid;
        readonly ComboBox _step;
        readonly NumericUpDown _offset;
        readonly Func<IEnumerable<ScheduleEntry>> _all;
        readonly Func<ScheduleEntry?> _selected;
        readonly Action _changed;
        readonly HashSet<double> _extra = new();
        List<double> _times = new();
        List<WeekTime>? _snapshot;

        public EntryCalendar(ScheduleWindow window, WeekGrid grid, Grid header, ComboBox step, NumericUpDown offset, TextBox addBox, Button addButton,
            Button clear, Func<IEnumerable<ScheduleEntry>> all, Func<ScheduleEntry?> selected, Action changed)
        {
            _grid = grid;
            _step = step;
            _offset = offset;
            _all = all;
            _selected = selected;
            _changed = changed;
            step.ItemsSource = Steps.Select(m => m == 60 ? L.T("hour") : L.F("{0} min", m)).ToList();
            step.SelectedIndex = 1;
            offset.Value = 0;
            step.SelectionChanged += (_, _) => Rebuild();
            offset.ValueChanged += (_, _) => Rebuild();
            Header(header, Week.Select(d => (DayShort(d), false)).ToList());

            grid.CellOf = Cell;
            grid.PaintStarted += () => _snapshot = _selected()?.Slots.Select(s => new WeekTime { Day = s.Day, Time = s.Time }).ToList();
            grid.Paint += (cells, on) =>
            {
                if (_selected() is not { } e || _snapshot == null) return;
                e.Slots = _snapshot.Select(s => new WeekTime { Day = s.Day, Time = s.Time }).ToList();
                foreach (var (col, row) in cells) e.Set(Week[col], TimeSpan.FromSeconds(_times[row]), on);
                window.ErrorText.Text = "";
            };
            grid.PaintEnded += () => _changed();
            addButton.Click += (_, _) =>
            {
                if (ScheduleTimes.Parse(addBox.Text) is not { } t)
                {
                    window.ErrorText.Text = L.F("\"{0}\" isn't a time (e.g. 12:59:55, or *:59:55 for every hour)", addBox.Text);
                    return;
                }
                var times = t.EveryHour ? Enumerable.Range(0, 24).Select(h => TimeSpan.FromHours(h) + t.Time).ToList() : [t.Time];
                foreach (var time in times) _extra.Add(time.TotalSeconds);
                // "*:59:55": a row every hour at that minute and second, with the selected item put at all of them, every day.
                if (t.EveryHour && _selected() is { } e)
                {
                    foreach (var day in Week)
                        foreach (var time in times)
                            e.Set(day, time, true);
                    _changed();
                }
                addBox.Text = "";
                window.ErrorText.Text = "";
                Rebuild();
            };
            clear.Click += (_, _) =>
            {
                if (_selected() is not { } e) return;
                e.Slots.Clear();
                _changed();
                Rebuild();
            };
        }

        public IReadOnlyList<double> Times => _times;

        public void Rebuild()
        {
            int minutes = Steps[Math.Max(0, _step.SelectedIndex)];
            int first = (int)(_offset.Value ?? 0) % minutes;
            var times = new SortedSet<double>(_extra);
            for (int m = first; m < 24 * 60; m += minutes) times.Add(m * 60);
            foreach (var e in _all())
                foreach (var s in e.Slots) times.Add(s.At.TotalSeconds);
            _times = times.ToList();
            _grid.Times = _times;
            _grid.Refresh();
        }

        GridCell Cell(int col, int row)
        {
            var day = Week[col];
            var time = TimeSpan.FromSeconds(_times[row]);
            var selected = _selected();
            var others = _all().Where(e => e != selected && e.Has(day, time)).Select(EntryName).ToList();
            bool on = selected?.Has(day, time) == true;
            string text = on ? EntryName(selected!) + (others.Count > 0 ? $" +{others.Count}" : "") : string.Join(", ", others);
            return new GridCell(text, on, selected != null, others.Count > 0);
        }
    }

    /// <summary>The day names above a calendar, lined up with its columns.</summary>
    static void Header(Grid header, IReadOnlyList<(string Text, bool Today)> days)
    {
        header.ColumnDefinitions.Clear();
        header.Children.Clear();
        header.ColumnDefinitions.Add(new ColumnDefinition(WeekGrid.TimeWidth, GridUnitType.Pixel));
        for (int i = 0; i < days.Count; i++)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var text = new TextBlock { Text = days[i].Text };
            text.Classes.Add("dayhead");
            text.Classes.Set("today", days[i].Today);
            Grid.SetColumn(text, i + 1);
            header.Children.Add(text);
        }
    }

    /// <summary>A day's short name in the interface language, at most three letters: Mon, Seg, Lun.</summary>
    static string DayShort(DayOfWeek day)
    {
        string name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day).TrimEnd('.');
        if (name.Length > 3) name = name[..3];
        return name.Length == 0 ? day.ToString()[..3] : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }

    static DateTime Monday(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    // ───────────────────────── Shared ─────────────────────────

    /// <summary>The start choices and the lock of an event or break, wired to the selected one.</summary>
    void WireStart(ListBox list, RadioButton soft, RadioButton hard, RadioButton end, NumericUpDown delay, ComboBox lockBox, Control delayPanel)
    {
        void Changed()
        {
            Edit<ScheduleEntry>(list, e =>
            {
                e.Start = hard.IsChecked == true ? EventStart.Hard : end.IsChecked == true ? EventStart.EndAt : EventStart.Soft;
                e.MaxDelayMinutes = (double)(delay.Value ?? 0);
                e.Lock = (ItemLock)Math.Max(0, lockBox.SelectedIndex);
            });
            delayPanel.IsEnabled = soft.IsChecked == true;
        }
        foreach (var r in new[] { soft, hard, end }) r.IsCheckedChanged += (_, _) => Changed();
        delay.ValueChanged += (_, _) => Changed();
        lockBox.SelectionChanged += (_, _) => Changed();
    }

    static void ShowStart(ScheduleEntry? e, RadioButton soft, RadioButton hard, RadioButton end, NumericUpDown delay, ComboBox lockBox, Control delayPanel)
    {
        var start = e?.Start ?? EventStart.Soft;
        soft.IsChecked = start == EventStart.Soft;
        hard.IsChecked = start == EventStart.Hard;
        end.IsChecked = start == EventStart.EndAt;
        delay.Value = (decimal)(e?.MaxDelayMinutes ?? 0);
        lockBox.SelectedIndex = (int)(e?.Lock ?? ItemLock.Free);
        delayPanel.IsEnabled = start == EventStart.Soft;
    }

    static string StartText(ScheduleEntry e) => (e.Start switch
    {
        EventStart.Hard => L.T("hard"),
        EventStart.EndAt => L.T("ends at its time"),
        _ => e.MaxDelayMinutes > 0 ? L.F("soft, ≤ {0:0} min late", e.MaxDelayMinutes) : L.T("soft"),
    }) + e.Lock switch
    {
        ItemLock.Locked => L.T(" · locked"),
        ItemLock.Movable => L.T(" · movable"),
        _ => "",
    };

    /// <summary>Applies an edit to the selected item of a list (not while the editor is being filled) and updates the lists.</summary>
    void Edit<T>(ListBox list, Action<T> change) where T : class
    {
        if (_loading || (list.SelectedItem as ScheduleRow)?.Item is not T item) return;
        change(item);
        (list.SelectedItem as ScheduleRow)?.Refresh();
        ErrorText.Text = SavedText.Text = "";
        if (item is AdClient or AdCampaign)
        {
            (ClientList.SelectedItem as ScheduleRow)?.Refresh();
            RefreshRows(_breakRows);
            ShowBooked();
            ShowPlaced();
            SpotGrid.Refresh();
        }
        if (item is AdBreak)
        {
            RefreshRows(_campaignRows);
            BreakGrid.Refresh();
            ShowSpotWeek();
            ShowPlaced();
        }
        if (item is ScheduledEvent) EventGrid.Refresh();
        if (item is PlaylistTemplate) TemplateGrid.Refresh();
    }

    static void RefreshRows(IEnumerable<ScheduleRow> rows)
    {
        foreach (var r in rows) r.Refresh();
    }

    static string EntryName(ScheduleEntry e) => string.IsNullOrWhiteSpace(e.Name) ? e is AdBreak ? L.T("Ad break") : L.T("Event") : e.Name.Trim();

    static string Plural(int n, string one, string many) => L.F(n == 1 ? one : many, n);

    /// <summary>"07:00, 13:00 · Mon–Fri" when every time is on the same days; else how many times a week.</summary>
    static string SlotsSummary(ScheduleEntry e)
    {
        if (e.Slots.Count == 0 && e.Dates.Count > 0) return DatesSummary(e);
        if (e.Slots.Count == 0) return L.T("not in the calendar yet");
        var byTime = e.Slots.GroupBy(s => s.At).OrderBy(g => g.Key).ToList();
        var days = byTime[0].Select(s => s.Day).ToHashSet();
        if (byTime.Any(g => !g.Select(s => s.Day).ToHashSet().SetEquals(days))) return L.F("{0} times a week", e.Slots.Count);
        string times = byTime.Count <= 4 ? string.Join(", ", byTime.Select(g => WeekGrid.Time(g.Key.TotalSeconds))) : L.F("{0} times", byTime.Count);
        return $"{times} · {DaysText(days)}" + (e.Dates.Count > 0 ? " · " + DatesSummary(e) : "");
    }

    /// <summary>"once: 20/10 14:00" (the next ones; those gone by when none is to come).</summary>
    static string DatesSummary(ScheduleEntry e)
    {
        var dates = e.Dates.Order().ToList();
        var coming = dates.Where(d => d >= DateTime.Now.AddHours(-1)).ToList();
        var shown = (coming.Count > 0 ? coming : dates.TakeLast(1)).ToList();
        string text = string.Join(", ", shown.Take(3).Select(d => d.ToString(d.Second == 0 ? "dd/MM HH:mm" : "dd/MM HH:mm:ss")));
        return L.F("once: {0}", shown.Count > 3 ? text + "…" : text);
    }

    static string DaysText(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0 || days.Count == 7) return L.T("every day");
        var set = days.ToHashSet();
        if (set.SetEquals(Week[..5])) return L.T("Mon–Fri");
        if (set.SetEquals(Week[5..])) return L.T("weekends");
        return string.Join(" ", Week.Where(set.Contains).Select(DayShort));
    }

    async Task<string?> PickFile(string title)
    {
        var start = Directory.Exists(_browseFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_browseFolder) : null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }, FilePickerFileTypes.All],
        });
        return files.FirstOrDefault()?.TryGetLocalPath() is { } path ? SharedPaths.ToLibrary(path) : null;
    }

    async Task<string?> PickFolder(string title)
    {
        var start = Directory.Exists(_browseFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_browseFolder) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, SuggestedStartLocation = start });
        return folders.FirstOrDefault()?.TryGetLocalPath() is { } path ? SharedPaths.ToLibrary(path) : null;
    }

    /// <summary>Checks that what is switched on is in the calendar, and the adverts' dates, before handing the schedule back.</summary>
    void Save()
    {
        SavedText.Text = "";
        foreach (var (entry, list, rows, tab) in _data.Events.Select(e => ((ScheduleEntry)e, EventList, _eventRows, 0))
                     .Concat(_data.Breaks.Select(b => ((ScheduleEntry)b, BreakList, _breakRows, 1)))
                     .Concat(_data.Templates.Select(t => ((ScheduleEntry)t, TemplateList, _templateRows, 3))))
        {
            if (!entry.Enabled || entry.Slots.Count > 0 || entry.Dates.Count > 0) continue;
            Tabs.SelectedIndex = tab;
            list.SelectedItem = rows.FirstOrDefault(r => r.Item == entry);
            ErrorText.Text = L.F("{0} isn't in the calendar yet: click the days and times it runs (or switch it off).",
                entry is PlaylistTemplate t ? TemplateName(t) : EntryName(entry));
            return;
        }
        foreach (var c in _data.Clients)
            foreach (var k in c.Campaigns)
                if (k.From is { } f && k.To is { } t && t < f)
                {
                    Tabs.SelectedIndex = 2;
                    ClientList.SelectedItem = _clientRows.FirstOrDefault(r => r.Item == c);
                    CampaignList.SelectedItem = _campaignRows.FirstOrDefault(r => r.Item == k);
                    ErrorText.Text = L.F("{0}: the last day is before the first.", CampaignName(k));
                    return;
                }
        if (_store == null)
        {
            ErrorText.Text = _offline ?? L.T("Not connected to the database: the schedule can't be saved.");
            return;
        }
        Store(_version);
    }

    /// <summary>Opens on the daily playlists.</summary>
    public void ShowDailyPlaylists()
    {
        if (DailyTab.IsVisible) Tabs.SelectedItem = DailyTab;
    }

    /// <summary>
    /// A change made from the daily playlists (a stream put in an hour, or taken out): saved straight away on its own
    /// (other changes not saved yet here stay so), and made here too. Returns why it wasn't saved, or null.
    /// </summary>
    string? ChangeNow(Action<ScheduleData> change)
    {
        if (_store == null) return _offline ?? L.T("Not connected to the database: the schedule can't be saved.");
        try
        {
            bool clean = !Changed;
            var (fresh, version) = _store.Load();
            change(fresh);
            long saved = _store.Save(fresh, version, Who);
            change(_data);
            if (_version == version) _version = saved; // nobody else saved since it was opened here: still the latest
            if (clean) _saved = Snapshot();
            var selected = (EventList.SelectedItem as ScheduleRow)?.Item;
            _eventRows = _data.Events.Select(EventRow).ToList();
            EventList.ItemsSource = _eventRows;
            EventList.SelectedItem = _eventRows.FirstOrDefault(r => r.Item == selected) ?? _eventRows.FirstOrDefault();
            return null;
        }
        catch (ScheduleStore.ChangedException)
        {
            return L.T("the schedule was saved elsewhere meanwhile: try again");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Who saved, for the next one who finds the schedule changed.</summary>
    string Who => $"{_user?.Name ?? Environment.UserName}@{Environment.MachineName}";

    /// <summary>Saves to the database; <paramref name="expected"/> null: over whatever is there.</summary>
    void Store(long? expected)
    {
        try
        {
            _version = _store!.Save(_data, expected, Who);
            _saved = Snapshot();
            ErrorText.Text = "";
            SavedText.Text = L.F("Saved at {0:HH:mm:ss}. The playout takes it within a few seconds.", DateTime.Now);
            if (_daily != null) _ = _daily.ScheduleSaved(); // the hours whose template changed are made again
        }
        catch (ScheduleStore.ChangedException ex)
        {
            _ = AskOverwrite(ex);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
        }
    }

    /// <summary>Someone else saved the schedule since it was opened here: theirs is replaced only if asked to.</summary>
    async Task AskOverwrite(ScheduleStore.ChangedException ex)
    {
        string by = ex.By.Length > 0 ? ex.By : L.T("another computer");
        if (await Dialogs.Confirm(this, L.T("Schedule changed"),
                L.F("The schedule was saved by {0} at {1:HH:mm} after you opened it. Save yours over it? Their changes would be lost (close and open the schedule again to see them).", by, ex.At),
                L.T("Save mine")))
            Store(null);
        else ErrorText.Text = L.T("Not saved: the schedule was changed elsewhere.");
    }
}
