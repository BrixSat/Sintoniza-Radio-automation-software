using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
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
/// Edits the schedule: events at fixed times (news, programmes, streams), ad breaks, and the adverts booked into
/// them. Works on a copy: Save hands it back, Cancel drops it.
/// </summary>
public partial class ScheduleWindow : Window
{
    static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    readonly ScheduleData _data;
    readonly string _browseFolder;
    readonly Dictionary<ScheduleEntry, string> _timesText = new(); // as typed, checked on Save
    readonly ToggleButton[] _eventDays, _breakDays, _spotDays;
    List<ScheduleRow> _eventRows = new(), _breakRows = new(), _spotRows = new();
    bool _loading;

    // Parameterless constructor for the XAML previewer.
    public ScheduleWindow() : this(new ScheduleData(), "") { }

    public ScheduleWindow(ScheduleData data, string browseFolder)
    {
        InitializeComponent();
        _data = data;
        _browseFolder = browseFolder;
        foreach (var e in data.Events) _timesText[e] = ScheduleTimes.Join(e.Times);
        foreach (var b in data.Breaks) _timesText[b] = ScheduleTimes.Join(b.Times);

        _eventDays = DayButtons(EventDays, () => Edit<ScheduledEvent>(EventList, e => e.Days = Days(_eventDays!)));
        _breakDays = DayButtons(BreakDays, () => Edit<AdBreak>(BreakList, b => b.Days = Days(_breakDays!)));
        _spotDays = DayButtons(SpotDays, () => Edit<AdSpot>(SpotList, s => s.Days = Days(_spotDays!)));

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
    }

    // ───────────────────────── Events ─────────────────────────

    void WireEvents()
    {
        EventList.SelectionChanged += (_, _) => ShowEvent();
        AddEventButton.Click += (_, _) =>
        {
            var e = new ScheduledEvent { Name = "News" };
            _data.Events.Add(e);
            _timesText[e] = "";
            _eventRows = _eventRows.Append(EventRow(e)).ToList();
            EventList.ItemsSource = _eventRows;
            EventList.SelectedIndex = _eventRows.Count - 1;
            EventNameBox.Focus();
            EventNameBox.SelectAll();
        };
        DeleteEventButton.Click += async (_, _) =>
        {
            if (EventList.SelectedItem is not ScheduleRow { Item: ScheduledEvent e } row) return;
            if (!await Dialogs.Confirm(this, "Delete event", $"Delete \"{EntryName(e)}\"?", "Delete")) return;
            _data.Events.Remove(e);
            _timesText.Remove(e);
            int i = _eventRows.IndexOf(row);
            _eventRows = _eventRows.Where(r => r != row).ToList();
            EventList.ItemsSource = _eventRows;
            EventList.SelectedIndex = Math.Min(i, _eventRows.Count - 1);
        };
        EventNameBox.TextChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.Name = EventNameBox.Text?.Trim() ?? "");
        EventEnabledBox.IsCheckedChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.Enabled = EventEnabledBox.IsChecked == true);
        EventPathBox.TextChanged += (_, _) =>
        {
            Edit<ScheduledEvent>(EventList, e => e.Path = EventPathBox.Text?.Trim() ?? "");
            ShowEventPathKind();
        };
        EventFileButton.Click += async (_, _) => { if (await PickFile("Event: file to play") is { } p) EventPathBox.Text = p; };
        EventFolderButton.Click += async (_, _) => { if (await PickFolder("Event: folder whose newest file plays") is { } p) EventPathBox.Text = p; };
        EventMaxAgeBox.ValueChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.MaxAgeMinutes = (int)(EventMaxAgeBox.Value ?? 0));
        EventStreamMinutesBox.ValueChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.StreamMinutes = (double)(EventStreamMinutesBox.Value ?? 0));
        EventTimesBox.TextChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => SetTimes(e, EventTimesBox.Text));
        EventHardBox.IsCheckedChanged += (_, _) => Edit<ScheduledEvent>(EventList, e => e.Hard = EventHardBox.IsChecked == true);
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
        EventTimesBox.Text = e != null ? _timesText.GetValueOrDefault(e, "") : "";
        ShowDays(_eventDays, e?.Days);
        EventHardBox.IsChecked = e?.Hard == true;
        EventSoftBox.IsChecked = e?.Hard != true;
        _loading = false;
        ShowEventPathKind();
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
            stream ? "An internet stream." :
            folder ? "A folder: its newest audio file plays (e.g. a news bulletin that is downloaded there)." :
            File.Exists(path) ? "A file." : "Not found on this computer.";
    }

    ScheduleRow EventRow(ScheduledEvent e) => new(e, () =>
    {
        string what = PlayoutEngine.IsUrl(e.Path) ? "stream" : Directory.Exists(e.Path) ? "newest in folder" : Path.GetFileName(e.Path);
        return (EntryName(e), $"{TimesSummary(e)} · {DaysText(e.Days)} · {(e.Hard ? "hard" : "soft")}{(what.Length > 0 ? " · " + what : "")}", e.Enabled);
    });

    // ───────────────────────── Ad breaks ─────────────────────────

    void WireBreaks()
    {
        BreakList.SelectionChanged += (_, _) => ShowBreak();
        AddBreakButton.Click += (_, _) =>
        {
            var b = new AdBreak { Name = "Ad break" };
            _data.Breaks.Add(b);
            _timesText[b] = "";
            _breakRows = _breakRows.Append(BreakRow(b)).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = _breakRows.Count - 1;
            ShowSpot(); // the new break is offered to the adverts
            BreakTimesBox.Focus();
        };
        DeleteBreakButton.Click += async (_, _) =>
        {
            if (BreakList.SelectedItem is not ScheduleRow { Item: AdBreak b } row) return;
            // Adverts booked only into this break would otherwise play in every break (none ticked): switched off.
            int only = _data.Spots.Count(s => s.Breaks.Count == 1 && s.Breaks[0] == b.Id);
            string more = only > 0 ? $" {Plural(only, "advert")} booked only into this break will be switched off." : "";
            if (!await Dialogs.Confirm(this, "Delete ad break", $"Delete \"{EntryName(b)}\"?{more}", "Delete")) return;
            _data.Breaks.Remove(b);
            _timesText.Remove(b);
            foreach (var s in _data.Spots)
                if (s.Breaks.Remove(b.Id) && s.Breaks.Count == 0) s.Enabled = false;
            int i = _breakRows.IndexOf(row);
            _breakRows = _breakRows.Where(r => r != row).ToList();
            BreakList.ItemsSource = _breakRows;
            BreakList.SelectedIndex = Math.Min(i, _breakRows.Count - 1);
            ShowSpot();
            RefreshRows(_spotRows);
        };
        BreakNameBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Name = BreakNameBox.Text?.Trim() ?? "");
        BreakEnabledBox.IsCheckedChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Enabled = BreakEnabledBox.IsChecked == true);
        BreakTimesBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => SetTimes(b, BreakTimesBox.Text));
        BreakHardBox.IsCheckedChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Hard = BreakHardBox.IsChecked == true);
        BreakIntroBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Intro = BreakIntroBox.Text?.Trim() ?? "");
        BreakOutroBox.TextChanged += (_, _) => Edit<AdBreak>(BreakList, b => b.Outro = BreakOutroBox.Text?.Trim() ?? "");
        BreakIntroButton.Click += async (_, _) => { if (await PickFile("Opening jingle") is { } p) BreakIntroBox.Text = p; };
        BreakOutroButton.Click += async (_, _) => { if (await PickFile("Closing jingle") is { } p) BreakOutroBox.Text = p; };
        BreakIntroClear.Click += (_, _) => BreakIntroBox.Text = "";
        BreakOutroClear.Click += (_, _) => BreakOutroBox.Text = "";
    }

    void ShowBreak()
    {
        var b = (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak;
        BreakPanel.IsEnabled = b != null;
        _loading = true;
        BreakNameBox.Text = b?.Name ?? "";
        BreakEnabledBox.IsChecked = b?.Enabled ?? true;
        BreakTimesBox.Text = b != null ? _timesText.GetValueOrDefault(b, "") : "";
        ShowDays(_breakDays, b?.Days);
        BreakHardBox.IsChecked = b?.Hard == true;
        BreakSoftBox.IsChecked = b?.Hard != true;
        BreakIntroBox.Text = b?.Intro ?? "";
        BreakOutroBox.Text = b?.Outro ?? "";
        _loading = false;
        ShowBooked();
    }

    void ShowBooked()
    {
        var b = (BreakList.SelectedItem as ScheduleRow)?.Item as AdBreak;
        BreakBookedText.Text = b == null ? "" : $"Booked today: {Plural(BookedToday(b), "advert")}";
    }

    int BookedToday(AdBreak b) => _data.Spots.Count(s => s.PlaysIn(b, DateTime.Today));

    ScheduleRow BreakRow(AdBreak b) => new(b, () =>
        (EntryName(b), $"{TimesSummary(b)} · {DaysText(b.Days)} · {(b.Hard ? "hard" : "soft")} · {Plural(BookedToday(b), "advert")} today", b.Enabled));

    // ───────────────────────── Adverts ─────────────────────────

    void WireSpots()
    {
        SpotList.SelectionChanged += (_, _) => ShowSpot();
        AddSpotButton.Click += async (_, _) =>
        {
            if (await PickFile("Advert") is not { } path) return;
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
            if (!await Dialogs.Confirm(this, "Delete advert", $"Delete \"{SpotName(s)}\" from the schedule? The file stays on disk.", "Delete")) return;
            _data.Spots.Remove(s);
            int i = _spotRows.IndexOf(row);
            _spotRows = _spotRows.Where(r => r != row).ToList();
            SpotList.ItemsSource = _spotRows;
            SpotList.SelectedIndex = Math.Min(i, _spotRows.Count - 1);
            RefreshRows(_breakRows);
            ShowBooked();
        };
        SpotUpButton.Click += (_, _) => MoveSpot(-1);
        SpotDownButton.Click += (_, _) => MoveSpot(1);
        SpotPathBox.TextChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Path = SpotPathBox.Text?.Trim() ?? "");
        SpotFileButton.Click += async (_, _) => { if (await PickFile("Advert") is { } p) SpotPathBox.Text = p; };
        SpotAdvertiserBox.TextChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Advertiser = SpotAdvertiserBox.Text?.Trim() ?? "");
        SpotEnabledBox.IsCheckedChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.Enabled = SpotEnabledBox.IsChecked == true);
        SpotFromBox.SelectedDateChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.From = SpotFromBox.SelectedDate?.Date);
        SpotToBox.SelectedDateChanged += (_, _) => Edit<AdSpot>(SpotList, s => s.To = SpotToBox.SelectedDate?.Date);
        SpotClearDatesButton.Click += (_, _) =>
        {
            SpotFromBox.SelectedDate = null;
            SpotToBox.SelectedDate = null;
        };
    }

    void ShowSpot()
    {
        var s = (SpotList.SelectedItem as ScheduleRow)?.Item as AdSpot;
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
        SpotNoBreaks.IsVisible = _data.Breaks.Count == 0;
        _loading = false;
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
            (null, null) => "no end date",
            ({ } f, null) => $"from {f:yyyy-MM-dd}",
            (null, { } t) => $"until {t:yyyy-MM-dd}",
            ({ } f, { } t) => $"{f:yyyy-MM-dd} to {t:yyyy-MM-dd}",
        };
        string breaks = s.Breaks.Count == 0 ? "every break" : string.Join(", ", _data.Breaks.Where(b => s.Breaks.Contains(b.Id)).Select(EntryName));
        bool over = s.To is { } to && to.Date < DateTime.Today;
        return (SpotName(s), $"{Path.GetFileName(s.Path)} · {dates}{(over ? " (ended)" : "")} · {DaysText(s.Days)} · {breaks}", s.Enabled && !over);
    });

    static string SpotName(AdSpot s) => s.Advertiser.Length > 0 ? s.Advertiser : Path.GetFileNameWithoutExtension(s.Path);

    // ───────────────────────── Shared ─────────────────────────

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
        }
        if (item is AdBreak) RefreshRows(_spotRows);
    }

    static void RefreshRows(IEnumerable<ScheduleRow> rows)
    {
        foreach (var r in rows) r.Refresh();
    }

    void SetTimes(ScheduleEntry entry, string? text)
    {
        _timesText[entry] = text ?? "";
        entry.Times = ScheduleTimes.Split(text).Times;
    }

    static string EntryName(ScheduleEntry e) => string.IsNullOrWhiteSpace(e.Name) ? e is AdBreak ? "Ad break" : "Event" : e.Name.Trim();

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    static string TimesSummary(ScheduleEntry e)
    {
        if (e.Times.Count == 0) return "no times yet";
        var text = ScheduleTimes.Join(e.Times);
        return text.Length > 40 ? text[..38] + "…" : text;
    }

    static string DaysText(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 0 || days.Count == 7) return "every day";
        var set = days.ToHashSet();
        if (set.SetEquals(Week[..5])) return "Mon–Fri";
        if (set.SetEquals(Week[5..])) return "weekends";
        return string.Join(" ", Week.Where(set.Contains).Select(d => d.ToString()[..3]));
    }

    ToggleButton[] DayButtons(StackPanel panel, Action changed)
    {
        var buttons = Week.Select(d => new ToggleButton { Content = d.ToString()[..3], Tag = d }).ToArray();
        foreach (var b in buttons)
        {
            b.Classes.Add("day");
            b.IsCheckedChanged += (_, _) => changed();
            panel.Children.Add(b);
        }
        return buttons;
    }

    void ShowDays(ToggleButton[] buttons, IReadOnlyCollection<DayOfWeek>? days)
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
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = AudioFiles.Patterns }, FilePickerFileTypes.All],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    async Task<string?> PickFolder(string title)
    {
        var start = Directory.Exists(_browseFolder) ? await StorageProvider.TryGetFolderFromPathAsync(_browseFolder) : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, SuggestedStartLocation = start });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>Checks the times (and that each entry has some) before handing the schedule back.</summary>
    void Save()
    {
        foreach (var (entry, list, tab) in _data.Events.Select(e => ((ScheduleEntry)e, EventList, 0))
                     .Concat(_data.Breaks.Select(b => ((ScheduleEntry)b, BreakList, 1))))
        {
            var (times, bad) = ScheduleTimes.Split(_timesText.GetValueOrDefault(entry, ""));
            string? problem = bad.Count > 0 ? $"\"{string.Join(", ", bad)}\" isn't a time (use 07:30, 7:30:15 or *:30)"
                : entry.Enabled && times.Count == 0 ? "it has no times" : null;
            if (problem == null) continue;
            Tabs.SelectedIndex = tab;
            list.SelectedItem = (tab == 0 ? _eventRows : _breakRows).FirstOrDefault(r => r.Item == entry);
            ErrorText.Text = $"{EntryName(entry)}: {problem}.";
            return;
        }
        foreach (var s in _data.Spots)
            if (s.From is { } f && s.To is { } t && t < f)
            {
                Tabs.SelectedIndex = 2;
                SpotList.SelectedItem = _spotRows.FirstOrDefault(r => r.Item == s);
                ErrorText.Text = $"{SpotName(s)}: the last day is before the first.";
                return;
            }
        Close(_data);
    }
}
