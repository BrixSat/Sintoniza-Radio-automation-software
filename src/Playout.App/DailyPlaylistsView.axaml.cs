using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The daily playlists (a tab of Sintoniza Pro): made ahead from the hour templates, as they will play.
/// A day's hours, and for the selected hour each song and jingle in order with its start time, and the ad breaks and
/// events booked in it (with their adverts, as booked now). Songs can be replaced, moved, removed and put in from the
/// library; an hour changed by hand is never made again by itself. The playout plays these playlists.
/// </summary>
public partial class DailyPlaylistsView : UserControl
{
    /// <summary>An hour that is about to play isn't made again by itself when its template changes.</summary>
    static readonly TimeSpan KeepNear = TimeSpan.FromMinutes(40);

    readonly LibraryDb? _db;
    readonly LibraryService? _library;
    readonly ScheduleStore? _scheduleStore;
    readonly AppSettings _settings;

    /// <summary>
    /// Only voice tracks may be recorded (a presenter): the hours can be looked at and listened to, not changed.
    /// </summary>
    public bool VoiceOnly
    {
        get => _voiceOnly;
        set
        {
            _voiceOnly = value;
            foreach (var c in new Control[] { StreamButton, SegueButton, TagsButton, ReplaceButton, UpButton, DownButton, RemoveButton, GenerateHourButton, GenerateDayButton })
                c.IsVisible = !value;
            LibraryPanel.IsVisible = LibrarySplitter.IsVisible = !value; // nothing to put in from the library
            if (LibraryPanel.Parent is Grid grid && grid.ColumnDefinitions.Count == 5)
            {
                grid.ColumnDefinitions[3].Width = new Avalonia.Controls.GridLength(value ? 0 : 12);
                grid.ColumnDefinitions[4].Width = new Avalonia.Controls.GridLength(value ? 0 : 420);
            }
            ShowButtons();
        }
    }
    bool _voiceOnly;
    /// <summary>Changes the schedule and saves it straight away (Sintoniza Pro does it); returns why not, or null.</summary>
    readonly Func<Action<ScheduleData>, string?>? _changeSchedule;
    ScheduleData _schedule;
    long _scheduleVersion = -1;
    IReadOnlyList<LibraryCategory> _categories = [];
    DateTime _day = DateTime.Today;
    Dictionary<DateTime, PlanHour> _hours = new();
    Dictionary<DateTime, double> _lengths = new();
    DateTime? _hour;
    List<PlanItem> _items = new();
    readonly DispatcherTimer _refresh;
    bool _busy;

    // Parameterless constructor for the XAML previewer.
    public DailyPlaylistsView() : this(null, null, null, new AppSettings(), new ScheduleData(), null) { }

    internal DailyPlaylistsView(LibraryDb? db, LibraryService? library, ScheduleStore? scheduleStore, AppSettings settings, ScheduleData schedule, string? error,
        Func<Action<ScheduleData>, string?>? changeSchedule = null)
    {
        _changeSchedule = changeSchedule;
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _library = library;
        _scheduleStore = scheduleStore;
        _settings = settings;
        _schedule = schedule;
        if (library != null)
        {
            Tracks.Attach(library);
            Tracks.AddRequested += (paths, asNext) => _ = InsertFromLibrary(paths);
            Tracks.ListenRequested += path => Listen(path, System.IO.Path.GetFileNameWithoutExtension(path));
            Tracks.EditTagsRequested += path => _ = EditTags(path);
        }
        if (db == null)
        {
            StatusText.Text = L.F("The database can't be reached: {0}", error);
            GenerateDayButton.IsEnabled = GenerateHourButton.IsEnabled = false;
        }

        DayBox.SelectedDate = _day;
        DayBox.SelectedDateChanged += (_, _) => { if (DayBox.SelectedDate is { } d && d.Date != _day) _ = ShowDay(d.Date); };
        PrevDayButton.Click += (_, _) => DayBox.SelectedDate = _day.AddDays(-1);
        NextDayButton.Click += (_, _) => DayBox.SelectedDate = _day.AddDays(1);
        TodayButton.Click += (_, _) => DayBox.SelectedDate = DateTime.Today;
        HourList.SelectionChanged += (_, _) => { if (HourList.SelectedItem is PlanHourRow r && r.Start != _hour) _ = ShowHour(r.Start); };
        GenerateDayButton.Click += async (_, _) => await GenerateDay();
        GenerateHourButton.Click += async (_, _) => await GenerateHour();
        RemoveButton.Click += async (_, _) => await RemoveSelected();
        StreamButton.Click += async (_, _) => await AddStream();
        UpButton.Click += async (_, _) => await Move(-1);
        DownButton.Click += async (_, _) => await Move(1);
        ReplaceButton.Click += async (_, _) => await Replace();
        ItemList.SelectionChanged += (_, _) => ShowButtons();
        ItemList.DoubleTapped += (_, e) => { if ((e.Source as Control)?.DataContext is PlanRow row) ListenTo(row); };
        SegueButton.Click += async (_, _) => await EditSegue();
        VoiceButton.Click += async (_, _) => await RecordVoiceTrack();
        TagsButton.Click += async (_, _) => { if (SelectedRow() is { Path.Length: > 0 } row) await EditTags(row.Path); };
        ListenButton.Click += (_, _) => ListenOrPause();
        ListenStopButton.Click += (_, _) => StopListening();
        ListenSlider.AddHandler(PointerReleasedEvent, (_, _) => _player?.Seek(ListenSlider.Value), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || e.Source is TextBox) return;
            e.Handled = true;
            ListenOrPause();
        };
        _listenTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => ShowListening());
        ItemList.KeyDown += async (_, e) => { if (e.Key == Key.Delete) { e.Handled = true; await RemoveSelected(); } };

        // What the playout (or another studio) made meanwhile, and a schedule saved since: seen within a minute.
        // The hour on air every 15 seconds (what played, what is on air); the whole day every minute.
        int ticks = 0;
        _refresh = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, async (_, _) =>
        {
            if (_busy) return;
            if (++ticks % 4 == 0) await ShowDay(_day, quiet: true);
            else if (_hour is { } h && h <= DateTime.Now.AddHours(1) && h.AddHours(2) > DateTime.Now && _db is { } db)
            {
                // Only when something new went on air.
                var latest = await Task.Run(() => db.PlayedFiles(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(1)).LastOrDefault());
                if (latest != _latestPlay)
                {
                    _latestPlay = latest;
                    await ShowHour(h, quiet: true);
                }
            }
        });
        AttachedToVisualTree += async (_, _) =>
        {
            await ShowDay(_day);
            _refresh.Start();
        };
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>The window closes: listening stops, the sound card is let go.</summary>
    public void Stop()
    {
        _refresh.Stop();
        StopListening();
        _deck?.Dispose();
        _deck = null;
    }

    /// <summary>The schedule was saved (templates may have changed): the day is read and made again where needed.</summary>
    public async Task ScheduleSaved()
    {
        if (_db != null) await ShowDay(_day);
    }

    Window? Owner => TopLevel.GetTopLevel(this) as Window;

    // ───────────────────────── Listening ─────────────────────────

    PreviewDeck? _deck;
    PreviewPlayer? _player;
    string? _playerPath;
    readonly DispatcherTimer _listenTimer;

    PlanRow? SelectedRow() => ItemList.SelectedItems?.OfType<PlanRow>().FirstOrDefault();

    void ListenTo(PlanRow row)
    {
        if (row.Path.Length > 0) Listen(row.Path, row.HasArtist ? $"{row.Artist} - {row.Title}" : row.Title);
    }

    /// <summary>Space and ▶: the selected item, or pause / carry on with the one playing.</summary>
    void ListenOrPause()
    {
        if (_player != null && (SelectedRow() is not { } row || row.Path == _playerPath))
        {
            if (_player.IsPlaying) _player.Pause(); else _player.Play();
            ShowListening();
            return;
        }
        if (SelectedRow() is { } selected) ListenTo(selected);
    }

    /// <summary>Plays a file on the Preview output (Settings → Audio), never on the on-air cards.</summary>
    void Listen(string path, string title)
    {
        StopListening();
        try
        {
            _deck ??= new PreviewDeck(_settings.Routing, MainWindow.ForcedDeviceFromArgs());
            ListenWarning.Text = L.T("The Preview output is the on-air one (Settings → Audio): this would be heard on air.");
            ListenWarning.IsVisible = _deck.PreviewIsOnAir;
            _player = _deck.Open(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Can't listen: {0}", ex.Message);
            return;
        }
        if (_player == null)
        {
            StatusText.Text = L.F("Can't listen: {0} can't be opened", System.IO.Path.GetFileName(path));
            return;
        }
        _playerPath = path;
        ListenTitle.Text = title;
        ListenSlider.Maximum = Math.Max(1, _player.Length);
        _player.Play();
        _listenTimer.Start();
        ShowListening();
    }

    void StopListening()
    {
        _player?.Dispose();
        _player = null;
        _playerPath = null;
        _listenTimer.Stop();
        ListenTitle.Text = "";
        ListenSlider.Value = 0;
        ShowListening();
    }

    void ShowListening()
    {
        bool playing = _player?.IsPlaying == true;
        ListenButton.Content = playing ? "❚❚" : "▶";
        double at = _player?.Position ?? 0, length = _player?.Length ?? 0;
        ListenTime.Text = $"{Format.Duration(at)} / {Format.Duration(length)}";
        if (_player != null && !ListenSlider.IsPointerOver) ListenSlider.Value = at;
    }

    // ───────────────────────── Voice tracks ─────────────────────────

    /// <summary>Removes items, and the voice tracks that belong to them.</summary>
    static void RemoveWithVoiceTracks(List<PlanItem> items, IReadOnlyCollection<PlanItem> remove)
    {
        var gone = remove.ToHashSet();
        for (int i = 0; i < items.Count; i++)
            if (items[i].FollowsPrevious && i > 0 && gone.Contains(items[i - 1])) gone.Add(items[i]);
        items.RemoveAll(gone.Contains);
    }

    /// <summary>
    /// The song a voice track goes after (the selected one, or the one before a selected voice track), its voice track
    /// if it has one, and the row after them (what comes next).
    /// </summary>
    (PlanRow Before, PlanItem? Existing, PlanRow After)? VoiceRows()
    {
        if (SelectedRow() is not { Item: { } item } row || ItemList.ItemsSource is not List<PlanRow> rows) return null;
        int i = rows.IndexOf(row);
        if (item.Kind == TrackKind.Voice)
        {
            if (i < 1 || rows[i - 1].Item == null) return null;
            row = rows[--i];
        }
        if (row.Path.Length == 0 || row.Kind == TrackKind.Stream) return null;
        var existing = i + 1 < rows.Count && rows[i + 1].Item is { FollowsPrevious: true } vt ? vt : null;
        int next = existing != null ? i + 2 : i + 1;
        return next < rows.Count && rows[next] is { Path.Length: > 0, Kind: not TrackKind.Stream } after ? (row, existing, after) : null;
    }

    static string _voiceInput = "";

    /// <summary>
    /// Records a voice track after the selected song (or again, over the one it has): saved in the station's voice
    /// tracks folder, put in the library (Takes) and in the hour right after the song, starting over its ending.
    /// </summary>
    async Task RecordVoiceTrack()
    {
        if (_db is not { } db || _hour is not { } hour || VoiceRows() is not var (before, existing, after) || Owner is not { } owner) return;
        StopListening();
        // The station's folder for voice tracks: chosen the first time.
        var stations = new StationStore(db);
        var (station, version) = await Task.Run(stations.Load);
        string folder = station?.VoiceTrackFolder ?? "";
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            var picked = await owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = L.T("Where should voice tracks be kept? (a folder every studio can reach)"),
            });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } chosen) return;
            folder = chosen;
            var updated = station?.Clone() ?? StationSettings.From(_settings);
            updated.VoiceTrackFolder = folder;
            try { await Task.Run(() => stations.Save(updated, version, $"{Environment.UserName}@{Environment.MachineName}")); }
            catch (Exception ex) { StatusText.Text = L.F("The folder isn't kept for next time: {0}", ex.Message); }
        }
        try { _deck ??= new PreviewDeck(_settings.Routing, MainWindow.ForcedDeviceFromArgs()); }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Can't listen: {0}", ex.Message);
            return;
        }
        var deck = _deck;
        var host = new SegueHost
        {
            Open = deck.Open, GetCues = db.GetCues, SaveCues = (_, _) => Task.CompletedTask, LoadWaveform = Waveforms.LoadAsync,
            Crossfade = CrossfadeBetween, PreviewIsOnAir = deck.PreviewIsOnAir,
        };
        string Name(PlanRow r) => r.HasArtist ? $"{r.Artist} - {r.Title}" : r.Title;
        var window = new VoiceTrackWindow(host, new SegueItem(before.Path, Name(before), before.Kind), new SegueItem(after.Path, Name(after), after.Kind),
            folder, _voiceInput.Length > 0 ? _voiceInput : _settings.Mic?.Device ?? "");
        var take = await window.ShowDialog<VoiceTake?>(owner);
        _voiceInput = window.Input;
        if (take == null || before.Item is not { } song) return;
        await InsertVoiceTrack(db, hour, song, take);
    }

    /// <summary>Saves a take as the voice track after <paramref name="song"/> in the hour shown (in place of one it had).</summary>
    async Task InsertVoiceTrack(LibraryDb db, DateTime hour, PlanItem song, VoiceTake take)
    {
        try
        {
            var (file, item) = await Task.Run(() => SaveVoiceTrack(db, hour, song, take));
            await Edit(items =>
            {
                int i = items.FindIndex(x => x.Id == song.Id);
                if (i < 0) return;
                while (i + 1 < items.Count && items[i + 1].FollowsPrevious) items.RemoveAt(i + 1); // the take it replaces
                // The song plays to its end now, with the voice over its last seconds.
                double length = db.GetByPath(song.Path)?.Duration is > 0 and var d ? d : AudioProbe.Duration(song.Path) ?? song.Seconds;
                var cues = db.GetCues(song.Path);
                double plays = Math.Min(cues.CueOut ?? length, length) - (cues.CueIn ?? 0);
                items[i].Seconds = Math.Max(1, plays - take.Overlap);
                items.Insert(i + 1, item);
            });
            StatusText.Text = L.F("Voice track saved: {0}", Path.GetFileName(file));
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("The voice track isn't saved: {0}", ex.Message);
        }
    }

    /// <summary>
    /// The take as a voice track: renamed after the hour and the song, put in the library under Takes with its Mix
    /// point (where the next song comes in), and the plan item for the hour.
    /// </summary>
    static (string File, PlanItem Item) SaveVoiceTrack(LibraryDb db, DateTime hour, PlanItem song, VoiceTake take)
    {
        string safe = string.Concat(song.Display.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' or ':' ? '_' : ch)).Trim();
        if (safe.Length > 60) safe = safe[..60];
        string folder = Path.GetDirectoryName(take.File)!;
        string file = Path.Combine(folder, $"VT {hour:yyyy-MM-dd HHmm} {safe}.mp3");
        for (int n = 2; File.Exists(file); n++) file = Path.Combine(folder, $"VT {hour:yyyy-MM-dd HHmm} {safe} ({n}).mp3");
        File.Move(take.File, file);
        db.Refresh(file);
        if (db.GetByPath(file) is { } track && db.Categories().FirstOrDefault(c => c.Role == "takes") is { } takes) db.SetCategory([track.Id], takes.Id);
        if (take.NextAt is { } mix) db.SetCues(file, new CuePoints(Mix: mix));
        var item = new PlanItem
        {
            Kind = TrackKind.Voice, Path = file, Title = L.F("Voice track after {0}", song.Title), Artist = "",
            TrackId = db.GetByPath(file)?.Id, Seconds = Math.Max(0.5, take.NextAt ?? take.Length), Overlap = take.Overlap, FollowsPrevious = true,
        };
        return (file, item);
    }

    // ───────────────────────── Segue ─────────────────────────

    static readonly WaveformLoader Waveforms = new();

    /// <summary>The selected item and the one after it, when both are files.</summary>
    (PlanRow First, PlanRow Next)? SegueRows()
    {
        if (SelectedRow() is not { Path.Length: > 0 } row || ItemList.ItemsSource is not List<PlanRow> rows) return null;
        int i = rows.IndexOf(row);
        return i >= 0 && i + 1 < rows.Count && rows[i + 1] is { Path.Length: > 0, Kind: not TrackKind.Stream } next ? (row, next) : null;
    }

    double CrossfadeBetween(TrackKind a, TrackKind b) =>
        a is TrackKind.Music or TrackKind.Stream && b is TrackKind.Music or TrackKind.Stream ? _settings.Crossfade : _settings.OtherCrossfade;

    /// <summary>The segue editor for the selected item and the next; saved to the library (the playout takes it within a minute).</summary>
    async Task EditSegue()
    {
        if (_db is not { } db || SegueRows() is not var (first, next) || Owner is not { } owner) return;
        StopListening();
        try { _deck ??= new PreviewDeck(_settings.Routing, MainWindow.ForcedDeviceFromArgs()); }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Can't listen: {0}", ex.Message);
            return;
        }
        var deck = _deck;
        var host = new SegueHost
        {
            Open = deck.Open,
            GetCues = db.GetCues,
            SaveCues = (path, cues) => Task.Run(() =>
            {
                // A file outside the library is added to it, so its cue points are kept (and shared).
                if (!db.SetCues(path, cues) && !cues.IsEmpty)
                {
                    db.Refresh(path);
                    db.SetCues(path, cues);
                }
                // The planned hours from now on: how long it plays before the next item.
                double length = db.GetByPath(path)?.Duration is > 0 and var d ? d : AudioProbe.Duration(path) ?? 0;
                var kind = path == first.Path ? first.Kind : next.Kind;
                if (length > 0) new PlanStore(db).UpdateSeconds(path, Plays(cues, length, kind), Hour(DateTime.Now));
            }),
            LoadWaveform = Waveforms.LoadAsync,
            Crossfade = CrossfadeBetween,
            PreviewIsOnAir = deck.PreviewIsOnAir,
        };
        string Name(PlanRow r) => r.HasArtist ? $"{r.Artist} - {r.Title}" : r.Title;
        if (await new SegueEditorWindow(host, new SegueItem(first.Path, Name(first), first.Kind), new SegueItem(next.Path, Name(next), next.Kind))
                .ShowDialog<bool>(owner))
        {
            StatusText.Text = L.T("Segue saved. The playout takes it within a minute.");
            if (_hour is { } hour) await ShowHour(hour);
        }
    }

    /// <summary>How long an item plays before the next starts, from its cue points (as the daily playlists count it).</summary>
    double Plays(CuePoints cues, double length, TrackKind kind)
    {
        double start = cues.CueIn ?? 0, end = Math.Min(cues.CueOut ?? length, length);
        if (cues.Mix is { } mix && mix > start && mix < end) return mix - start;
        double plays = end - start, fade = CrossfadeBetween(kind, kind);
        return Math.Max(1, plays - Math.Min(fade, plays / 2));
    }

    // ───────────────────────── Tags ─────────────────────────

    async Task EditTags(string path)
    {
        if (!File.Exists(path))
        {
            StatusText.Text = L.F("Not found: {0}", path);
            return;
        }
        if (Owner is { } owner && await new TagEditorWindow(SaveTags, path).ShowDialog<bool>(owner))
        {
            StatusText.Text = L.F("Tags saved: {0}", System.IO.Path.GetFileName(path));
            _library?.NotifyChanged();
            if (_hour is { } hour) await ShowHour(hour);
        }
    }

    /// <summary>
    /// Writes a file's tags (refused while it is on air: the playout has it open), then brings the library and the
    /// planned hours up to date with them.
    /// </summary>
    async Task SaveTags(string path, TrackTags tags, byte[]? cover)
    {
        if (_db is { } db && await Task.Run(() =>
            {
                if (db.LastStarted(path) is not { } started) return false;
                double length = db.GetByPath(path)?.Duration is > 0 and var d ? d : AudioProbe.Duration(path) ?? 600;
                return DateTime.UtcNow - started < TimeSpan.FromSeconds(length + 30);
            }))
            throw new InvalidOperationException(L.T("This file is on air now. Edit its tags once it has finished."));
        if (_playerPath == path) StopListening(); // let go of the file while it is rewritten
        await Task.Run(() => Tags.Write(path, tags, cover));
        if (_db is { } library)
            await Task.Run(() =>
            {
                library.Refresh(path);
                if (!string.IsNullOrWhiteSpace(tags.Title)) new PlanStore(library).UpdateTitles(path, tags.Title.Trim(), tags.Artist.Trim());
            });
    }

    PlaylistGenerator.Options Options => new(_settings.Crossfade, _settings.OtherCrossfade,
        TimeSpan.FromHours(_settings.FillSongHours), TimeSpan.FromMinutes(_settings.FillArtistMinutes));

    static DateTime Hour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0);

    // ───────────────────────── The day ─────────────────────────

    /// <summary>Shows a day: its hours made first if they aren't (from now on), and the template changes caught up.</summary>
    /// <summary>The day on show read again (changed elsewhere: another computer, the playout keeping time).</summary>
    public Task Refresh() => ShowDay(_day, quiet: true);

    async Task ShowDay(DateTime day, bool quiet = false)
    {
        if (_db is not { } db) return;
        _day = day;
        _busy = true;
        if (!quiet) StatusText.Text = L.T("Reading the day…");
        try
        {
            var (hours, lengths, made, problems, categories) = await Task.Run(() =>
            {
                ReloadSchedule();
                var store = new PlanStore(db);
                var now = DateTime.Now;
                var from = day.AddDays(1) <= Hour(now) ? day.AddDays(1) : day < Hour(now) ? Hour(now) : day;
                var problems = new List<string>();
                int made = from < day.AddDays(1)
                    ? PlanMaker.Ensure(store, _schedule, Options, from, day.AddDays(1), now + KeepNear, out problems)
                    : 0;
                var hours = store.Hours(day, day.AddDays(1));
                var items = store.Items(day, day.AddDays(1));
                var generator = new PlaylistGenerator(db, _schedule, Options);
                var lengths = Enumerable.Range(0, 24).Select(h => day.AddHours(h)).ToDictionary(h => h,
                    h => (items.GetValueOrDefault(h)?.Where(i => !i.Optional).Sum(i => i.Seconds) ?? 0) + generator.Entries(h).Sum(e => e.Seconds));
                return (hours, lengths, made, problems, db.Categories());
            });
            _hours = hours;
            _lengths = lengths;
            _categories = categories;
            DayText.Text = day == DateTime.Today ? L.T("today") : day == DateTime.Today.AddDays(1) ? L.T("tomorrow") : "";
            var rows = Enumerable.Range(0, 24).Select(h => new PlanHourRow(day.AddHours(h), _hours.GetValueOrDefault(day.AddHours(h)),
                _schedule.TemplateAt(day.AddHours(h)), _lengths.GetValueOrDefault(day.AddHours(h)))).ToList();
            HourList.ItemsSource = rows;
            var select = _hour is { } h0 && h0.Date == day ? h0 : day == DateTime.Today ? Hour(DateTime.Now) : day.AddHours(7);
            HourList.SelectedItem = rows.FirstOrDefault(r => r.Start == select);
            if (_hour != null && _hour.Value.Date == day) await ShowHour(_hour.Value, quiet);
            if (!quiet)
                StatusText.Text = made > 0 ? L.F("{0} hours made", made) : problems.Count > 0 ? string.Join(" · ", problems.Distinct()) : "";
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("The daily playlists can't be read: {0}", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The schedule (templates, breaks, events) as saved in Sintoniza Pro, when it changed.</summary>
    void ReloadSchedule()
    {
        if (_scheduleStore == null) return;
        try
        {
            long version = _scheduleStore.Version();
            if (version == _scheduleVersion) return;
            (_schedule, _scheduleVersion) = _scheduleStore.Load();
        }
        catch { /* the one loaded carries on */ }
    }

    // ───────────────────────── The hour ─────────────────────────

    (string Path, DateTime At) _latestPlay;

    async Task ShowHour(DateTime hour, bool quiet = false)
    {
        // The same hour again: the list stays where it was scrolled to.
        var scroll = ItemList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = hour == _hour ? scroll?.Offset : null;
        if (_db is not { } db) return;
        _hour = hour;
        var template = _schedule.TemplateAt(hour);
        HourTitle.Text = L.F("{0:dddd dd/MM} · {0:HH}:00", hour).ToUpperInvariant();
        try
        {
            var (items, entries, plays) = await Task.Run(() => (new PlanStore(db).Items(hour), new PlaylistGenerator(db, _schedule, Options).Entries(hour),
                db.PlayedFiles(hour.AddHours(-3).ToUniversalTime(), hour.AddHours(3).ToUniversalTime())));
            if (_hour != hour) return; // another hour chosen meanwhile
            var selected = SelectedItems().Select(i => i.Id).ToHashSet();
            _items = items;
            var rows = Timeline(hour, items, entries);
            string onAir = MarkPlayed(rows, plays);
            ItemList.ItemsSource = rows;
            foreach (var r in rows.Where(r => r.Item != null && selected.Contains(r.Item.Id))) ItemList.SelectedItems?.Add(r);
            if (offset is { } o && scroll != null) Dispatcher.UIThread.Post(() => scroll.Offset = o, DispatcherPriority.Loaded);

            var record = _hours.GetValueOrDefault(hour);
            OnAirText.Text = onAir;
            OnAirText.IsVisible = onAir.Length > 0;
            HourInfo.Text = (template != null ? L.F("Template {0}", TemplateName(template)) : L.T("No template placed here: fill music plays"))
                + (record?.Edited == true ? " · " + L.T("changed by hand") : "")
                + (record != null && !record.Edited && record.Signature != PlaylistGenerator.Signature(template) ? " · " + L.T("its template changed since it was made") : "")
                + (record != null ? " · " + L.F("made {0:dd/MM HH:mm} on {1}", record.GeneratedAt.ToLocalTime(), record.GeneratedBy) : "");
            double songs = items.Where(i => !i.Optional).Sum(i => i.Seconds), breaks = entries.Sum(e => e.Seconds);
            TotalText.Text = items.Count == 0 && entries.Count == 0 ? "" : L.F("Songs and jingles {0} + ad breaks and events {1} = {2} (optional at the end: {3})",
                Format.Duration(songs), Format.Duration(breaks), Format.Duration(songs + breaks), Format.Duration(items.Where(i => i.Optional).Sum(i => i.Seconds)))
                + (rows.Count(r => r.IsOver) is > 0 and var over ? " · " + L.F(over == 1 ? "{0} item past the end of the hour won't play" : "{0} items past the end of the hour won't play", over) : "");
            EmptyText.Text = record == null ? (hour < Hour(DateTime.Now) ? L.T("Not made: it was before this playlist manager ran.") : L.T("Not made yet."))
                : L.T("Nothing planned in this hour: the playout plays fill music.");
            EmptyText.IsVisible = rows.Count == 0;
        }
        catch (Exception ex)
        {
            if (!quiet) StatusText.Text = L.F("The hour can't be read: {0}", ex.Message);
        }
        ShowButtons();
    }

    /// <summary>
    /// The hour in order: the planned items one after another, and each break or event after the item on air at its
    /// time (as the playout starts them), with its own items.
    /// </summary>
    List<PlanRow> Timeline(DateTime hour, List<PlanItem> items, List<PlannedEntry> entries)
    {
        var rows = new List<PlanRow>();
        var at = hour;
        var waiting = new Queue<PlannedEntry>(entries.OrderBy(e => Due(e)));
        void Entries(bool all)
        {
            while (waiting.Count > 0 && (all || Due(waiting.Peek()) <= at))
            {
                var e = waiting.Dequeue();
                if (e.Parts.Count == 0)
                {
                    rows.Add(PlanRow.Scheduled(at, new PlannedPart("", TrackKind.Commercial, L.T("nothing to play (no adverts booked or no file)"), null, 0), e));
                    continue;
                }
                foreach (var p in e.Parts)
                {
                    rows.Add(PlanRow.Scheduled(at, p, e));
                    at = at.AddSeconds(p.Seconds);
                }
            }
        }
        foreach (var item in items)
        {
            Entries(false);
            rows.Add(PlanRow.Planned(at, item, Source(item, hour)));
            at = at.AddSeconds(item.Seconds);
        }
        Entries(true);
        // A song that would start after the hour has ended (an hour made longer than the hour by hand): the playout
        // leaves it out so the next hour starts on time.
        foreach (var r in rows.Where(r => r.Item is { Optional: false } && r.Kind != TrackKind.Commercial && r.Start >= hour.AddHours(1).AddSeconds(20)))
            r.IsOver = true;
        return rows;
    }

    /// <summary>
    /// What of the hour already went on air (the play history every studio writes), in order, and the item on air now.
    /// Returns a line for the hour's header: what is on air and how far ahead or behind the plan the playout is.
    /// </summary>
    static string MarkPlayed(List<PlanRow> rows, List<(string Path, DateTime At)> plays)
    {
        var used = new bool[plays.Count];
        var last = DateTime.MinValue;
        foreach (var row in rows.Where(r => r.Path.Length > 0))
        {
            int i = Enumerable.Range(0, plays.Count).FirstOrDefault(i => !used[i] && plays[i].Path == row.Path && plays[i].At >= last, -1);
            if (i < 0) continue;
            used[i] = true;
            last = plays[i].At;
            row.PlayedAt = plays[i].At.ToLocalTime();
        }
        if (plays.Count == 0) return "";
        var latest = plays[^1];
        var onAir = rows.LastOrDefault(r => r.PlayedAt != null && r.Path == latest.Path && r.PlayedAt == latest.At.ToLocalTime());
        if (onAir == null || DateTime.Now - onAir.PlayedAt!.Value > TimeSpan.FromSeconds(onAir.Seconds + 30)) return "";
        onAir.IsOnAir = true;
        double minutes = (onAir.Start - onAir.PlayedAt.Value).TotalMinutes;
        string drift = Math.Abs(minutes) < 1.5 ? L.T("on time")
            : minutes > 0 ? L.F("{0:0} min ahead of the plan", minutes) : L.F("{0:0} min behind the plan", -minutes);
        return L.F("▶ On air now: {0} (planned {1:HH:mm}), {2}", onAir.HasArtist ? $"{onAir.Artist} - {onAir.Title}" : onAir.Title, onAir.Start, drift);
    }

    static DateTime Due(PlannedEntry e) => e.Entry.Start == EventStart.EndAt ? e.At.AddSeconds(-e.Seconds) : e.At;

    string Source(PlanItem item, DateTime hour)
    {
        if (item.Kind == TrackKind.Voice) return item.Overlap > 0 ? L.F("voice track · over the last {0:0.0} s", item.Overlap) : L.T("voice track");
        if (item.Line == null) return L.T("added by hand");
        if (item.Line == PlanItem.KeptTime) return L.T("added on air to keep time");
        var line = _schedule.TemplateAt(hour)?.Lines.ElementAtOrDefault(item.Line.Value);
        string what = item.CategoryId is { } c && _categories.Any(x => x.Id == c) ? CategoryTree.PathOf(_categories, c)
            : line?.Source == LineSource.Take ? ScheduleWindow.TakeName(line.Folder)
            : line?.Source == LineSource.Folder ? Path.GetFileName(line.Folder.TrimEnd('/', '\\')) : line?.CategoryName ?? "";
        return L.F("line {0}", item.Line.Value + 1) + (what.Length > 0 ? " · " + what : "");
    }

    static string TemplateName(PlaylistTemplate t) => string.IsNullOrWhiteSpace(t.Name) ? L.T("Playlist") : t.Name.Trim();

    List<PlanItem> SelectedItems() =>
        ItemList.SelectedItems?.OfType<PlanRow>().Where(r => r.Item != null).Select(r => r.Item!).ToList() ?? new();

    /// <summary>Streams put in this day's hours (once, at a time), selected: these can be taken out here.</summary>
    List<(Guid Entry, DateTime At)> SelectedOnce() =>
        ItemList.SelectedItems?.OfType<PlanRow>().Where(r => r.Once != null).Select(r => r.Once!.Value).Distinct().ToList() ?? new();

    void ShowButtons()
    {
        var selected = SelectedItems();
        bool open = _hour is { } h && h >= Hour(DateTime.Now) && _db != null;
        UpButton.IsEnabled = DownButton.IsEnabled = open && selected.Count > 0;
        RemoveButton.IsEnabled = open && (selected.Count > 0 || (_changeSchedule != null && SelectedOnce().Count > 0));
        StreamButton.IsEnabled = open && _changeSchedule != null;
        ReplaceButton.IsEnabled = open && selected.Count == 1 && selected[0].Line is >= 0;
        TagsButton.IsEnabled = SelectedRow() is { Path.Length: > 0 };
        SegueButton.IsEnabled = SegueRows() != null;
        VoiceButton.IsEnabled = open && VoiceRows() != null;
        GenerateHourButton.IsEnabled = open;
        GenerateDayButton.IsEnabled = _db != null && _day.AddDays(1) > DateTime.Now;
        Tracks.IsEnabled = open && !_voiceOnly;
    }

    // ───────────────────────── Changes ─────────────────────────

    /// <summary>Changes the selected hour's items, saves them as changed by hand, and shows the hour again.</summary>
    async Task Edit(Action<List<PlanItem>> change)
    {
        if (_db is not { } db || _hour is not { } hour || _busy) return;
        var items = _items.ToList();
        change(items);
        var record = _hours.GetValueOrDefault(hour) ?? new PlanHour
        {
            Start = hour, TemplateId = _schedule.TemplateAt(hour)?.Id, Signature = PlaylistGenerator.Signature(_schedule.TemplateAt(hour)),
            GeneratedAt = DateTime.UtcNow, GeneratedBy = Environment.MachineName,
        };
        record.Edited = true;
        try
        {
            await Task.Run(() => new PlanStore(db).Save(record, items));
            _hours[hour] = record;
            StatusText.Text = hour <= DateTime.Now.AddMinutes(30)
                ? L.T("Saved. This hour may be in the on-air playlist already: changes there are made in the playout.")
                : L.T("Saved.");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Not saved: {0}", ex.Message);
            return;
        }
        await ShowHour(hour);
        UpdateHourRow(hour);
    }

    void UpdateHourRow(DateTime hour)
    {
        if (HourList.ItemsSource is not List<PlanHourRow> rows) return;
        int i = rows.FindIndex(r => r.Start == hour);
        if (i < 0) return;
        _lengths[hour] = _items.Where(x => !x.Optional).Sum(x => x.Seconds) + (ItemList.ItemsSource as List<PlanRow>)?.Where(r => r.FromSchedule).Sum(r => r.Seconds) ?? 0;
        var copy = rows.ToList();
        copy[i] = new PlanHourRow(hour, _hours.GetValueOrDefault(hour), _schedule.TemplateAt(hour), _lengths[hour]);
        HourList.ItemsSource = copy;
        HourList.SelectedIndex = i;
    }

    async Task Move(int by)
    {
        var selected = SelectedItems();
        if (selected.Count == 0) return;
        await Edit(items =>
        {
            // An item and its voice track move together, past whole blocks.
            var blocks = new List<List<PlanItem>>();
            foreach (var item in items)
                if (item.FollowsPrevious && blocks.Count > 0) blocks[^1].Add(item);
                else blocks.Add([item]);
            var moving = blocks.Where(b => b.Any(selected.Contains)).ToHashSet();
            var order = by < 0 ? blocks.Where(moving.Contains).ToList() : blocks.Where(moving.Contains).Reverse().ToList();
            foreach (var block in order)
            {
                int i = blocks.IndexOf(block), to = i + by;
                if (to < 0 || to >= blocks.Count || moving.Contains(blocks[to])) continue;
                (blocks[i], blocks[to]) = (blocks[to], blocks[i]);
            }
            items.Clear();
            items.AddRange(blocks.SelectMany(b => b));
        });
    }

    /// <summary>Another song from the same template line, not one already in the hour.</summary>
    async Task Replace()
    {
        if (_db is not { } db || _hour is not { } hour || SelectedItems() is not [var item] || item.Line is not (>= 0 and var index)) return;
        var template = _schedule.TemplateAt(hour);
        if (template?.Lines.ElementAtOrDefault(index) is not { } line)
        {
            StatusText.Text = L.T("Its template line isn't there any more: make the hour again, or remove it.");
            return;
        }
        var next = template.Lines[(index + 1) % template.Lines.Count];
        var avoid = _items.Select(i => i.Path).ToHashSet();
        var at = hour.AddSeconds(_items.TakeWhile(i => i != item).Sum(i => i.Seconds));
        var replacement = await Task.Run(() =>
        {
            var generator = new PlaylistGenerator(db, _schedule, Options);
            generator.Remember(at, new PlanStore(db).Items(hour.AddHours(-3), hour.AddHours(3)));
            return generator.Another(line, next, at, avoid, template);
        });
        if (replacement == null)
        {
            StatusText.Text = L.T("No other song for this line.");
            return;
        }
        replacement.Line = item.Line;
        replacement.Optional = item.Optional;
        await Edit(items =>
        {
            int i = items.IndexOf(item);
            items[i] = replacement;
            // Its voice track talked about the song that went.
            while (i + 1 < items.Count && items[i + 1].FollowsPrevious) items.RemoveAt(i + 1);
        });
    }

    /// <summary>Songs from the library, after the selected item (at the end of the hour if none).</summary>
    async Task InsertFromLibrary(IReadOnlyList<string> paths)
    {
        if (_db is not { } db || _hour is null) return;
        var added = await Task.Run(() => paths.Where(File.Exists).Select(p =>
        {
            var t = db.GetByPath(p);
            double length = t == null ? AudioProbe.Duration(p) ?? 0 : (t.Duration > 0 ? t.Duration : AudioProbe.Duration(p) ?? 0);
            var kind = TrackClassifier.Classify(p);
            double fade = kind == TrackKind.Music ? _settings.Crossfade : _settings.OtherCrossfade;
            return new PlanItem
            {
                Kind = kind, Path = p, Title = t?.Title is { Length: > 0 } title ? title : Path.GetFileNameWithoutExtension(p), Artist = t?.Artist ?? "",
                TrackId = t?.Id, CategoryId = t?.CategoryId, Seconds = Math.Max(1, length - Math.Min(fade, length / 2)),
            };
        }).Where(i => i.Seconds > 1).ToList());
        if (added.Count == 0) return;
        var after = SelectedItems().LastOrDefault();
        await Edit(items => items.InsertRange(after == null ? items.Count : items.IndexOf(after) + 1, added));
    }

    async Task RemoveSelected()
    {
        var once = SelectedOnce();
        if (once.Count > 0 && _changeSchedule != null)
        {
            var names = once.Select(o => _schedule.Events.FirstOrDefault(e => e.Id == o.Entry)?.Name).OfType<string>().Distinct().ToList();
            if (Owner is not { } owner || !await Dialogs.Confirm(owner, L.T("Take out"),
                    L.F("Take {0} out of the playlist?", string.Join(", ", names.Select(n => $"\"{n}\""))), L.T("Take out")))
                return;
            await ChangeSchedule(data =>
            {
                foreach (var (id, at) in once)
                    if (data.Events.FirstOrDefault(e => e.Id == id) is { } e)
                    {
                        e.Dates.Remove(at);
                        if (e.Dates.Count == 0 && e.Slots.Count == 0) data.Events.Remove(e); // it was only for that time
                    }
            });
        }
        if (SelectedItems().Count > 0) await Edit(items => RemoveWithVoiceTracks(items, SelectedItems()));
    }

    /// <summary>
    /// Another station's stream at a time in the hour: an event on that date (Sintoniza Pro → Events shows it), so
    /// the playout starts it on time, on the dot or after the item on air, like any event.
    /// </summary>
    async Task AddStream()
    {
        if (_hour is not { } hour || Owner is not { } owner || _changeSchedule == null) return;
        var after = ItemList.SelectedItems?.OfType<PlanRow>().FirstOrDefault()?.Start;
        if (await new StreamDialog(hour, after).ShowDialog<StreamRequest?>(owner) is not { At: { } at } s) return;
        var id = Guid.NewGuid();
        if (await ChangeSchedule(data => data.Events.Add(new ScheduledEvent
            {
                Id = id, Name = s.Name, Path = s.Url, StreamMinutes = s.Seconds / 60, Start = s.Hard ? EventStart.Hard : EventStart.Soft, Dates = [at],
            })))
            StatusText.Text = L.F("{0} at {1:HH:mm:ss}. Saved in the schedule: the playout takes it within a few seconds.", s.Name, at);
    }

    /// <summary>Saves a change to the schedule straight away and shows the hour with it.</summary>
    async Task<bool> ChangeSchedule(Action<ScheduleData> change)
    {
        if (_changeSchedule == null) return false;
        if (_changeSchedule(change) is { } problem)
        {
            StatusText.Text = L.F("Not saved: {0}", problem);
            return false;
        }
        _scheduleVersion = -1; // read again, with the change
        await ShowDay(_day, quiet: true); // the hour with it
        return true;
    }

    // ───────────────────────── Making again ─────────────────────────

    async Task GenerateHour()
    {
        if (_db is not { } db || _hour is not { } hour) return;
        if (_hours.GetValueOrDefault(hour)?.Edited == true
            && (Owner is not { } owner || !await Dialogs.Confirm(owner, L.T("Make this hour again"), L.T("This hour was changed by hand. Make it again from its template, losing those changes?"), L.T("Make again"))))
            return;
        await Make([hour], evenIfEdited: true);
    }

    async Task GenerateDay()
    {
        if (_db is null) return;
        var from = _day > Hour(DateTime.Now) ? _day : Hour(DateTime.Now);
        var hours = Enumerable.Range(0, 24).Select(h => _day.AddHours(h)).Where(h => h >= from).ToList();
        int edited = hours.Count(h => _hours.GetValueOrDefault(h)?.Edited == true);
        string message = L.F("Make the {0} hours from {1:HH}:00 again from their templates (a new choice of songs)?", hours.Count, from)
            + (edited > 0 ? " " + L.F("The {0} changed by hand are kept (Make this hour again does those).", edited) : "");
        if (Owner is not { } dayOwner || !await Dialogs.Confirm(dayOwner, L.T("Make the day again"), message, L.T("Make again"))) return;
        await Make(hours, evenIfEdited: false);
    }

    async Task Make(List<DateTime> hours, bool evenIfEdited)
    {
        if (_db is not { } db || hours.Count == 0) return;
        _busy = true;
        StatusText.Text = L.T("Making…");
        try
        {
            var problems = new List<string>();
            int made = await Task.Run(() =>
            {
                ReloadSchedule();
                return PlanMaker.Make(new PlanStore(db), _schedule, Options, hours, problems, evenIfEdited);
            });
            StatusText.Text = L.F(made == 1 ? "{0} hour made" : "{0} hours made", made) + (problems.Count > 0 ? " · " + string.Join(" · ", problems.Distinct()) : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.F("Not made: {0}", ex.Message);
        }
        finally
        {
            _busy = false;
        }
        await ShowDay(_day, quiet: true);
    }
}

/// <summary>An hour in the day's list.</summary>
public sealed class PlanHourRow(DateTime start, PlanHour? hour, PlaylistTemplate? template, double seconds)
{
    public DateTime Start { get; } = start;
    public string HourText => $"{Start:HH}:00";
    public string TemplateName => template == null ? L.T("— no template —") : string.IsNullOrWhiteSpace(template.Name) ? L.T("Playlist") : template.Name.Trim();
    public bool NoTemplate => template == null;
    public string Mark => hour == null ? "" : hour.Edited ? "✎" : hour.Signature != PlaylistGenerator.Signature(template) ? "↻" : "";
    public string LengthText => hour == null ? "" : Format.Duration(seconds);
    public string Tip => hour == null ? L.T("Not made") : hour.Edited ? L.T("Changed by hand") : L.F("Made {0:dd/MM HH:mm}", hour.GeneratedAt.ToLocalTime());
    /// <summary>Hours gone by are dimmed.</summary>
    public double Opacity => Start.AddHours(1) <= DateTime.Now ? 0.45 : 1;
}

/// <summary>A row of the hour: a planned song or jingle, or an item of a break or event from the schedule.</summary>
public sealed class PlanRow
{
    public PlanItem? Item { get; private init; }
    public DateTime Start { get; private init; }
    public TrackKind Kind { get; private init; }
    public string Title { get; private init; } = "";
    public string Artist { get; private init; } = "";
    public string Source { get; private init; } = "";
    public double Seconds { get; private init; }
    /// <summary>The file it plays (for listening and its tags).</summary>
    public string Path { get; private init; } = "";
    public bool FromSchedule { get; private init; }
    public bool IsOptional { get; private init; }
    /// <summary>When it went on air (local), from the play history; null if it hasn't.</summary>
    public DateTime? PlayedAt { get; set; }
    public bool IsOnAir { get; set; }
    public string Mark => IsOnAir ? "▶" : PlayedAt != null ? "✓" : "";
    public Avalonia.Media.IBrush MarkBrush => IsOnAir ? Avalonia.Media.Brushes.OrangeRed : Avalonia.Media.Brushes.MediumSeaGreen;
    public double Opacity => IsOver ? 0.45 : PlayedAt != null && !IsOnAir ? 0.55 : 1;
    /// <summary>Starts after its hour has ended: the playout leaves it out.</summary>
    public bool IsOver { get; set; }
    public string? OverTip => IsOver ? L.F("Past the end of the hour: the playout leaves it out so {0:HH}:00 starts on time", Start.Date.AddHours(Start.Hour)) : null;
    public string? PlayedTip => PlayedAt is { } at ? L.F("On air at {0:HH:mm:ss}", at) : null;

    public static PlanRow Planned(DateTime start, PlanItem item, string source) => new()
    {
        Item = item, Start = start, Kind = item.Kind, Title = item.Title, Artist = item.Artist, Source = source, Seconds = item.Seconds, IsOptional = item.Optional,
        Path = item.Path,
    };

    /// <summary>An event put in this day only (a stream at a time): which, and when.</summary>
    public (Guid Entry, DateTime At)? Once { get; private init; }

    public static PlanRow Scheduled(DateTime start, PlannedPart part, PlannedEntry entry) => new()
    {
        Start = start, Kind = part.Kind, Title = part.Title, Artist = part.Advertiser ?? "", Source = entry.Label, Seconds = part.Seconds, FromSchedule = true,
        Path = part.Kind == TrackKind.Stream ? "" : part.Path,
        Once = entry.Entry.Dates.Contains(entry.At) ? (entry.Entry.Id, entry.At) : null,
    };

    public string StartText => Start.ToString("HH:mm:ss");
    public string LengthText => Seconds > 0 ? Format.Duration(Seconds) : "";
    public bool HasArtist => Artist.Length > 0;
    public string Icon => Kind switch
    {
        TrackKind.Jingle => "✦",
        TrackKind.Commercial => "€",
        TrackKind.Programme => "🎙",
        TrackKind.Voice => "🗣",
        TrackKind.Stream => "📡",
        _ => "♫",
    };
    public bool IsMusic => Kind == TrackKind.Music;
    public bool IsAdvert => Kind == TrackKind.Commercial;
    public bool IsJingle => Kind == TrackKind.Jingle;
    public bool IsProgramme => Kind is TrackKind.Programme or TrackKind.Voice;
    public bool IsStream => Kind == TrackKind.Stream;
}
