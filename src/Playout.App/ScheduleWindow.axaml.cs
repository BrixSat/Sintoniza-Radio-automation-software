using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Playout.App.Controls;
using Playout.Engine;

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
/// Edits the schedule on week calendars: events at set times (news, programmes, streams), ad breaks, and the
/// adverts placed in them. Works on a copy: Save hands it back, Cancel drops it.
/// </summary>
public partial class ScheduleWindow : Window
{
    static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    static readonly int[] Steps = [5, 10, 15, 20, 30, 60];

    readonly ScheduleData _data;
    readonly string _browseFolder;
    readonly ToggleButton[] _spotDays;
    readonly EntryCalendar _eventCalendar, _breakCalendar;
    readonly Dictionary<string, double> _lengths = new(); // advert file → seconds
    List<ScheduleRow> _eventRows = new(), _breakRows = new(), _spotRows = new();
    DateTime _week = Monday(DateTime.Today);
    List<double> _spotTimes = new();
    List<AdPlacement>? _spotSnapshot;
    bool _loading;

    // Parameterless constructor for the XAML previewer.
    public ScheduleWindow() : this(new ScheduleData(), "") { }

    public ScheduleWindow(ScheduleData data, string browseFolder)
    {
        InitializeComponent();
        Localization.Apply(this);
        _data = data;
        _data.Normalize();
        _browseFolder = browseFolder;

        _spotDays = DayButtons(SpotDays, () => Edit<AdSpot>(SpotList, s => s.Days = Days(_spotDays!)));
        _eventCalendar = new EntryCalendar(this, EventGrid, EventHeader, EventStepBox, EventOffsetBox, EventAddTimeBox, EventAddTimeButton, EventClearButton,
            () => _data.Events, () => (EventList.SelectedItem as ScheduleRow)?.Item as ScheduledEvent, () => (EventList.SelectedItem as ScheduleRow)?.Refresh());
        _breakCalendar = new EntryCalendar(this, BreakGrid, BreakHeader, BreakStepBox, BreakOffsetBox, BreakAddTimeBox, BreakAddTimeButton, BreakClearButton,
            () => _data.Breaks, () => (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak, () =>
            {
                (BreakList.SelectedItem as ScheduleRow)?.Refresh();
                ShowBooked();
                ShowSpotWeek();
            });

        WireEvents();
        WireBreaks();
        WireSpots();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close(null);

        _eventRows = data.Events.Select(EventRow).ToList();
        _breakRows = data.Breaks.Select(BreakRow).ToList();
        _spotRows = data.Spots.Select(SpotRow).ToList();
        EventList.ItemsSource = _eventRows;
        BreakList.ItemsSource = _breakRows;
        SpotList.ItemsSource = _spotRows;
        EventList.SelectedIndex = _eventRows.Count > 0 ? 0 : -1;
        BreakList.SelectedIndex = _breakRows.Count > 0 ? 0 : -1;
        SpotList.SelectedIndex = _spotRows.Count > 0 ? 0 : -1;
        ShowEvent();
        ShowBreak();
        ShowSpot();
        ShowSpotWeek();
        // Open at the morning, where most of the day's schedule is.
        Opened += (_, _) =>
        {
            foreach (var (scroll, grid) in new[] { (EventScroll, EventGrid), (BreakScroll, BreakGrid) })
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
        bool stream = PlayoutEngine.IsUrl(path);
        bool folder = !stream && path.Length > 0 && Directory.Exists(path);
        EventStreamPanel.IsVisible = stream;
        EventFolderPanel.IsVisible = folder;
        EventPathNote.Text = path.Length == 0 ? "" :
            stream ? L.T("An internet stream.") :
            folder ? L.T("A folder: its newest audio file plays (e.g. a news bulletin that is downloaded there).") :
            File.Exists(path) ? L.T("A file.") : L.T("Not found on this computer.");
    }

    ScheduleRow EventRow(ScheduledEvent e) => new(e, () =>
    {
        string what = PlayoutEngine.IsUrl(e.Path) ? L.T("stream") : Directory.Exists(e.Path) ? L.T("newest in folder") : Path.GetFileName(e.Path);
        return (EntryName(e), $"{SlotsSummary(e)} · {StartText(e)}{(what.Length > 0 ? " · " + what : "")}", e.Enabled);
    });

    // ───────────────────────── Ad breaks ─────────────────────────

    void WireBreaks()
    {
        BreakList.SelectionChanged += (_, _) => ShowBreak();
        AddBreakButton.Click += (_, _) =>
        {
            var b = new AdBreak { Name = L.T("Ad break") };
            _data.Breaks.Add(b);
            _breakRows = _breakRows.Append(BreakRow(b)).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = _breakRows.Count - 1;
            ShowSpot(); // the new break is offered to the adverts
            BreakNameBox.Focus();
            BreakNameBox.SelectAll();
        };
        DeleteBreakButton.Click += async (_, _) =>
        {
            if (BreakList.SelectedItem is not ScheduleRow { Item: AdBreak b } row) return;
            // Adverts booked only into this break would otherwise play in every break (none ticked): switched off.
            int only = _data.Spots.Count(s => s.Placements.Count == 0 && s.Breaks.Count == 1 && s.Breaks[0] == b.Id);
            string more = only > 0 ? L.F(" {0} booked only into this break will be switched off.", Plural(only, "{0} advert", "{0} adverts")) : "";
            if (!await Dialogs.Confirm(this, L.T("Delete ad break"), L.F("Delete \"{0}\"?{1}", EntryName(b), more), L.T("Delete"))) return;
            _data.Breaks.Remove(b);
            foreach (var s in _data.Spots)
                if (s.Breaks.Remove(b.Id) && s.Breaks.Count == 0 && s.Placements.Count == 0) s.Enabled = false;
            int i = _breakRows.IndexOf(row);
            _breakRows = _breakRows.Where(r => r != row).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = Math.Min(i, _breakRows.Count - 1);
            _breakCalendar.Rebuild();
            ShowSpot();
            ShowSpotWeek();
            RefreshRows(_spotRows);
        };
        BreakNameBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Name = BreakNameBox.Text?.Trim() ?? "");
        BreakEnabledBox.IsCheckedChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Enabled = BreakEnabledBox.IsChecked == true);
        BreakIntroBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Intro = BreakIntroBox.Text?.Trim() ?? "");
        BreakOutroBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Outro = BreakOutroBox.Text?.Trim() ?? "");
        BreakIntroButton.Click += async (_, _) => { if (await PickFile(L.T("Opening jingle")) is { } p) BreakIntroBox.Text = p; };
        BreakOutroButton.Click += async (_, _) => { if (await PickFile(L.T("Closing jingle")) is { } p) BreakOutroBox.Text = p; };
        BreakIntroClear.Click += (_, _) => BreakIntroBox.Text = "";
        BreakOutroClear.Click += (_, _) => BreakOutroBox.Text = "";
        WireStart(BreakList, BreakSoftBox, BreakHardBox, BreakEndBox, BreakDelayBox, BreakLockBox, BreakDelayPanel);
    }

    void ShowBreak()
    {
        var b = (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak;
        BreakPanel.IsEnabled = b != null;
        _loading = true;
        BreakNameBox.Text = b?.Name ?? "";
        BreakEnabledBox.IsChecked = b?.Enabled ?? true;
        ShowStart(b, BreakSoftBox, BreakHardBox, BreakEndBox, BreakDelayBox, BreakLockBox, BreakDelayPanel);
        BreakIntroBox.Text = b?.Intro ?? "";
        BreakOutroBox.Text = b?.Outro ?? "";
        _loading = false;
        ShowBooked();
        _breakCalendar.Rebuild();
    }

    void ShowBooked()
    {
        var b = (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak;
        BreakBookedText.Text = b == null ? "" : L.F("Today: {0}", Plural(BookedToday(b), "{0} advert", "{0} adverts"));
    }

    int BookedToday(AdBreak b) => _data.Spots.Count(s => b.TimesOn(DateTime.Today).Any(t => s.PlaysIn(b, t)));

    ScheduleRow BreakRow(AdBreak b) => new(b, () =>
        (EntryName(b), L.F("{0} · {1} · {2} today", SlotsSummary(b), StartText(b), Plural(BookedToday(b), "{0} advert", "{0} adverts")), b.Enabled));

    // ───────────────────────── Adverts ─────────────────────────

    void WireSpots()
    {
        SpotList.SelectionChanged += (_, _) =>
        {
            ShowSpot();
            SpotGrid.Refresh();
        };
        AddSpotButton.Click += async (_, _) =>
        {
            if (await PickFile(L.T("Advert")) is not { } path) return;
            var s = new AdSpot { Path = path, From = DateTime.Today };
            _data.Spots.Add(s);
            _spotRows = _spotRows.Append(SpotRow(s)).ToList();
            SpotList.ItemsSource = _spotRows;
            SpotList.SelectedIndex = _spotRows.Count - 1;
            RefreshRows(_breakRows);
            SpotAdvertiserBox.Focus();
        };
        DeleteSpotButton.Click += async (_, _) =>
        {
            if (SpotList.SelectedItem is not ScheduleRow { Item: AdSpot s } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete advert"), L.F("Delete \"{0}\" from the schedule? The file stays on disk.", SpotName(s)), L.T("Delete"))) return;
            _data.Spots.Remove(s);
            int i = _spotRows.IndexOf(row);
            _spotRows = _spotRows.Where(r => r != row).ToList();
            SpotList.ItemsSource = _spotRows;
            SpotList.SelectedIndex = Math.Min(i, _spotRows.Count - 1);
            RefreshRows(_breakRows);
            ShowBooked();
            SpotGrid.Refresh();
        };
        SpotUpButton.Click += (_, _) => MoveSpot(-1);
        SpotDownButton.Click += (_, _) => MoveSpot(1);
        SpotPathBox.TextChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Path = SpotPathBox.Text?.Trim() ?? "");
        SpotFileButton.Click += async (_, _) => { if (await PickFile(L.T("Advert")) is { } p) SpotPathBox.Text = p; };
        SpotAdvertiserBox.TextChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Advertiser = SpotAdvertiserBox.Text?.Trim() ?? "");
        SpotEnabledBox.IsCheckedChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Enabled = SpotEnabledBox.IsChecked == true);
        SpotFromBox.SelectedDateChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.From = SpotFromBox.SelectedDate?.Date);
        SpotToBox.SelectedDateChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.To = SpotToBox.SelectedDate?.Date);
        SpotClearDatesButton.Click += (_, _) =>
        {
            SpotFromBox.SelectedDate = null;
            SpotToBox.SelectedDate = null;
        };
        SpotClearPlacesButton.Click += (_, _) => Edit<AdSpot>(SpotList, s => s.Placements.Clear());

        // The week calendar of breaks.
        SpotGrid.CellOf = SpotCell;
        SpotGrid.PaintStarted += () => _spotSnapshot = SelectedSpot?.Placements.Select(Copy).ToList();
        SpotGrid.Paint += (cells, on) =>
        {
            if (SelectedSpot is not { } s || _spotSnapshot == null) return;
            s.Placements = _spotSnapshot.Select(Copy).ToList();
            foreach (var (col, row) in cells)
            {
                var date = _week.AddDays(col);
                var time = TimeSpan.FromSeconds(_spotTimes[row]);
                s.Placements.RemoveAll(p => p.At == time && (p.Date is { } d ? d.Date == date : p.Day == date.DayOfWeek));
                if (on)
                    s.Placements.Add(ThatDayBox.IsChecked == true
                        ? new AdPlacement { Date = date, Time = WeekTime.Text(time) }
                        : new AdPlacement { Day = date.DayOfWeek, Time = WeekTime.Text(time) });
            }
        };
        SpotGrid.PaintEnded += () => Edit<AdSpot>(SpotList, _ => { });
        WeekBackButton.Click += (_, _) => { _week = _week.AddDays(-7); ShowSpotWeek(); };
        WeekNextButton.Click += (_, _) => { _week = _week.AddDays(7); ShowSpotWeek(); };
        ThisWeekButton.Click += (_, _) => { _week = Monday(DateTime.Today); ShowSpotWeek(); };
    }

    AdSpot? SelectedSpot => (SpotList.SelectedItem as ScheduleRow)?.Item as AdSpot;

    static AdPlacement Copy(AdPlacement p) => new() { Day = p.Day, Date = p.Date, Time = p.Time };

    void ShowSpot()
    {
        var s = SelectedSpot;
        SpotPanel.IsEnabled = s != null;
        _loading = true;
        SpotPathBox.Text = s?.Path ?? "";
        SpotAdvertiserBox.Text = s?.Advertiser ?? "";
        SpotEnabledBox.IsChecked = s?.Enabled ?? true;
        SpotFromBox.SelectedDate = s?.From;
        SpotToBox.SelectedDate = s?.To;
        ShowDays(_spotDays, s?.Days);
        SpotBreaks.Children.Clear();
        foreach (var b in _data.Breaks)
        {
            var box = new CheckBox { Content = EntryName(b), IsChecked = s?.Breaks.Contains(b.Id) == true };
            box.IsCheckedChanged += (_, _) => Edit<AdSpot>(SpotList, spot =>
            {
                spot.Breaks.Remove(b.Id);
                if (box.IsChecked == true) spot.Breaks.Add(b.Id);
            });
            SpotBreaks.Children.Add(box);
        }
        SpotNoBreaksNote.IsVisible = _data.Breaks.Count == 0;
        _loading = false;
        ShowPlaced();
    }

    void ShowPlaced()
    {
        var s = SelectedSpot;
        int places = s?.Placements.Count ?? 0;
        SpotPlacedText.Text = s == null ? "" : places == 0
            ? L.T("Not placed in the calendar.")
            : L.F("Placed in {0} of the calendar ({1} every week, {2} on a date).", Plural(places, "{0} break", "{0} breaks"), s.Placements.Count(p => p.Date == null), s.Placements.Count(p => p.Date != null));
        SpotClearPlacesButton.IsVisible = places > 0;
        SpotRulesPanel.IsVisible = places == 0;
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

    GridCell SpotCell(int col, int row)
    {
        var date = _week.AddDays(col);
        var at = date + TimeSpan.FromSeconds(_spotTimes[row]);
        var breaks = _data.Breaks.Where(b => b.Enabled && b.TimesOn(date).Contains(at)).ToList();
        if (breaks.Count == 0) return new GridCell("", false, Enabled: false);
        var spots = _data.Spots.Where(s => breaks.Any(b => s.PlaysIn(b, at))).ToList();
        double seconds = spots.Sum(s => Length(s.Path));
        // Highlighted where it is placed; one not placed yet plays by its days and breaks, counted but not
        // highlighted, so the first click places it.
        bool on = SelectedSpot is { Placements.Count: > 0 } sel && spots.Contains(sel);
        string text = spots.Count == 0 ? "" : $"{spots.Count:00} | {TimeSpan.FromSeconds(Math.Round(seconds)):m\\:ss}";
        return new GridCell(text, on, SelectedSpot != null, spots.Count > 0);
    }

    /// <summary>An advert's length (read once from the file).</summary>
    double Length(string path)
    {
        if (_lengths.TryGetValue(path, out double s)) return s;
        s = File.Exists(path) ? AudioProbe.Duration(path) ?? 0 : 0;
        _lengths[path] = s;
        return s;
    }

    void MoveSpot(int delta)
    {
        if (SpotList.SelectedItem is not ScheduleRow { Item: AdSpot s } row) return;
        int i = _data.Spots.IndexOf(s), j = i + delta;
        if (j < 0 || j >= _data.Spots.Count) return;
        (_data.Spots[i], _data.Spots[j]) = (_data.Spots[j], _data.Spots[i]);
        _spotRows = _data.Spots.Select(x => _spotRows.First(r => r.Item == x)).ToList();
        SpotList.ItemsSource = _spotRows;
        SpotList.SelectedItem = row;
    }

    ScheduleRow SpotRow(AdSpot s) => new(s, () =>
    {
        string dates = (s.From, s.To) switch
        {
            (null, null) => L.T("no end date"),
            ({ } f, null) => L.F("from {0:yyyy-MM-dd}", f),
            (null, { } t) => L.F("until {0:yyyy-MM-dd}", t),
            ({ } f, { } t) => L.F("{0:yyyy-MM-dd} to {1:yyyy-MM-dd}", f, t),
        };
        string where = s.Placements.Count > 0 ? L.F("{0} in the calendar", Plural(s.Placements.Count, "{0} place", "{0} places"))
            : L.F("{0} · {1}", DaysText(s.Days), (s.Breaks.Count == 0 ? L.T("every break") : string.Join(", ", _data.Breaks.Where(b => s.Breaks.Contains(b.Id)).Select(EntryName))));
        bool over = s.To is { } to && to.Date < DateTime.Today;
        return (SpotName(s), L.F("{0} · {1}{2} · {3}", Path.GetFileName(s.Path), dates, (over ? L.T(" (ended)") : ""), where), s.Enabled && !over);
    });

    static string SpotName(AdSpot s) => s.Advertiser.Length > 0 ? s.Advertiser : Path.GetFileNameWithoutExtension(s.Path);

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
                if (ScheduleTimes.Parse(addBox.Text) is not { EveryHour: false } t)
                {
                    window.ErrorText.Text = L.F("\"{0}\" isn't a time (e.g. 12:59:55)", addBox.Text);
                    return;
                }
                _extra.Add(t.Time.TotalSeconds);
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
        ErrorText.Text = "";
        if (item is AdSpot)
        {
            RefreshRows(_breakRows);
            ShowBooked();
            ShowPlaced();
            SpotGrid.Refresh();
        }
        if (item is AdBreak)
        {
            RefreshRows(_spotRows);
            BreakGrid.Refresh();
            ShowSpotWeek();
        }
        if (item is ScheduledEvent) EventGrid.Refresh();
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
        if (e.Slots.Count == 0) return L.T("not in the calendar yet");
        var byTime = e.Slots.GroupBy(s => s.At).OrderBy(g => g.Key).ToList();
        var days = byTime[0].Select(s => s.Day).ToHashSet();
        if (byTime.Any(g => !g.Select(s => s.Day).ToHashSet().SetEquals(days))) return L.F("{0} times a week", e.Slots.Count);
        string times = byTime.Count <= 4 ? string.Join(", ", byTime.Select(g => WeekGrid.Time(g.Key.TotalSeconds))) : L.F("{0} times", byTime.Count);
        return $"{times} · {DaysText(days)}";
    }

    static string DaysText(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0 || days.Count == 7) return L.T("every day");
        var set = days.ToHashSet();
        if (set.SetEquals(Week[..5])) return L.T("Mon–Fri");
        if (set.SetEquals(Week[5..])) return L.T("weekends");
        return string.Join(" ", Week.Where(set.Contains).Select(DayShort));
    }

    ToggleButton[] DayButtons(Panel panel, Action changed)
    {
        var buttons = Week.Select(d => new ToggleButton { Content = DayShort(d), Tag = d }).ToArray();
        foreach (var b in buttons)
        {
            b.Classes.Add("day");
            b.IsCheckedChanged += (_, _) => changed();
            panel.Children.Add(b);
        }
        return buttons;
    }

    static void ShowDays(ToggleButton[] buttons, IReadOnlyCollection<DayOfWeek>? days)
    {
        foreach (var b in buttons) b.IsChecked = days?.Contains((DayOfWeek)b.Tag!) == true;
    }

    static List<DayOfWeek> Days(ToggleButton[] buttons) =>
        buttons.Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag!).ToList();

    async Task<string?> PickFile(string title)
    {
        var start = Directory.Exists(_browseFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_browseFolder) : null;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }, FilePickerFileTypes.All],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task<string?> PickFolder(string title)
    {
        var start = Directory.Exists(_browseFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_browseFolder) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, SuggestedStartLocation = start });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>Checks that what is switched on is in the calendar, and the adverts' dates, before handing the schedule back.</summary>
    void Save()
    {
        foreach (var (entry, list, tab) in _data.Events.Select(e => ((ScheduleEntry)e, EventList, 0))
                     .Concat(_data.Breaks.Select(b => ((ScheduleEntry)b, BreakList, 1))))
        {
            if (!entry.Enabled || entry.Slots.Count > 0) continue;
            Tabs.SelectedIndex = tab;
            list.SelectedItem = (tab == 0 ? _eventRows : _breakRows).FirstOrDefault(r => r.Item == entry);
            ErrorText.Text = L.F("{0} isn't in the calendar yet: click the days and times it runs (or switch it off).", EntryName(entry));
            return;
        }
        foreach (var s in _data.Spots)
            if (s.From is { } f && s.To is { } t && t < f)
            {
                Tabs.SelectedIndex = 2;
                SpotList.SelectedItem = _spotRows.FirstOrDefault(r => r.Item == s);
                ErrorText.Text = L.F("{0}: the last day is before the first.", SpotName(s));
                return;
            }
        Close(_data);
    }
}
