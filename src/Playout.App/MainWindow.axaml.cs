using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Playout.App.Controls;
using Playout.Engine;
using Track = Playout.Engine.Track;

namespace Playout.App;

public partial class MainWindow : Window
{
    readonly PlayoutEngine? _engine;
    readonly AppSettings _settings = AppSettings.Load();
    readonly ObservableCollection<PlaylistRow> _rows = new();
    readonly Dictionary<Guid, PlaylistRow> _rowsById = new();
    readonly DispatcherTimer _fastTimer;
    PlaylistSnapshot? _snapshot;
    Guid? _lastOnAirId;
    Guid? _waveTrackId;
    IDisposable? _nextFlash;

    static readonly DataFormat<Guid[]> RowDragFormat = DataFormat.CreateInProcessFormat<Guid[]>("RadioPlayout.Rows");
    int _slowTick;
    StreamState? _lastStreamState;
    DateTime _streamSince;
    DateTime _playedDay = DateTime.Today;
    int _playedToday;
    bool _settingsOpen;
    DateTime? _playlistEnds;
    bool _micKeyHeld;
    List<(string Id, KeyGesture? Gesture)> _shortcuts = new();
    readonly HashSet<Key> _keysDown = new(); // shortcut keys held down: auto-repeat must not toggle Pause or a cart over and over
    bool _micOn;

    public MainWindow()
    {
        InitializeComponent();
        Playlist.ItemsSource = _rows;

        try
        {
            var cues = new CueStore(Path.Combine(AppSettings.DataFolder, "cues.json"));
            _engine = new PlayoutEngine(_settings.Routing, cues, ForcedDeviceFromArgs());
            _engine.Log += msg => Dispatcher.UIThread.Post(() => LastLog.Text = msg);
            _engine.PlaylistChanged += snap => Dispatcher.UIThread.Post(() => ApplySnapshot(snap));
        }
        catch (Exception ex)
        {
            LastLog.Text = $"Audio engine failed to start: {ex.Message}";
            LastLog.Foreground = (IBrush)this.FindResource("OnAir")!;
        }

        BuildCartwall();
        LoadSettingsIntoUi();
        ApplyShortcuts();
        WireEvents();

        if (_engine != null)
        {
            _engine.CrossfadeSeconds = _settings.Crossfade;
            _engine.AutoAdvance = _settings.AutoAdvance;
            _engine.ConfigureMic(_settings.ActiveMic);
            _engine.ConfigureTimeSignal(_settings.TimeSignal);
            AddEntries(_settings.PlaylistItems);
        }

        _fastTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        _fastTimer.Start();
        Tick();
    }

    /// <summary><c>--device=N</c> sends every output to BASS device N, ignoring the routing in Settings (0 = "no sound", for testing).</summary>
    static int? ForcedDeviceFromArgs()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--device="));
        return arg != null && int.TryParse(arg["--device=".Length..], out int d) ? d : null;
    }

    // ───────────────────────── Wiring ─────────────────────────

    void WireEvents()
    {
        PlayButton.Click += (_, _) => PlayPressed();
        PauseButton.Click += (_, _) => _engine?.TogglePause();
        NextButton.Click += (_, _) => _engine?.Next();
        StopButton.Click += (_, _) => _engine?.Stop();
        MicButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty) UpdateMic();
        };

        AutoSwitch.IsCheckedChanged += (_, _) =>
        {
            _settings.AutoAdvance = AutoSwitch.IsChecked == true;
            if (_engine != null) _engine.AutoAdvance = _settings.AutoAdvance;
            ShowAutomationMode();
        };

        AddButton.Click += async (_, _) => await AddFilesFromPicker();
        AddStreamButton.Click += async (_, _) => await AddStream();
        OpenListButton.Click += async (_, _) => await OpenPlaylist();
        SaveListButton.Click += async (_, _) => await SavePlaylist();
        RemoveButton.Click += (_, _) => RemoveSelected();
        ClearButton.Click += (_, _) => _engine?.Clear();
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton.Click += (_, _) => MoveSelected(+1);

        Playlist.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is PlaylistRow row) _engine?.Play(row.Id);
        };
        var editCues = new MenuItem { Header = "Edit cue points…", InputGesture = new KeyGesture(Key.E, CommandModifiers) };
        editCues.Click += async (_, _) => await EditSelectedCues();
        var remove = new MenuItem { Header = "Remove", InputGesture = new KeyGesture(Key.Delete) };
        remove.Click += (_, _) => RemoveSelected();
        Playlist.ContextMenu = new ContextMenu { Items = { editCues, remove } };

        Playlist.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter && Playlist.SelectedItem is PlaylistRow row) { _engine?.Play(row.Id); e.Handled = true; }
        };

        // Rows can be dragged to reorder; drops also come from the music browser or the OS file manager.
        DragHelper.Attach(Playlist,
            () => Playlist.SelectedItems?.OfType<PlaylistRow>().ToList() ?? new(),
            rows => DragHelper.Data(RowDragFormat, rows.Select(r => r.Id).ToArray()),
            DragDropEffects.Move);

        Playlist.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(RowDragFormat) ? DragDropEffects.Move
                : e.DataTransfer.Contains(AudioFiles.DragFormat) || e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy
                : DragDropEffects.None;
            if (e.DragEffects != DragDropEffects.None) ShowDropIndicator(e);
        });
        Playlist.AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropIndicator.IsVisible = false);
        Playlist.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            DropIndicator.IsVisible = false;
            int insertAt = DropIndex(e);

            if (e.DataTransfer.TryGetValue(RowDragFormat) is { } ids)
            {
                _engine?.Move(ids, insertAt);
                return;
            }
            var paths = e.DataTransfer.TryGetValue(AudioFiles.DragFormat)
                ?? e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths is { Length: > 0 }) AddPaths(paths, insertAt >= _rows.Count ? -1 : insertAt);
        });

        Library.AddRequested += (paths, asNext) => AddPaths(paths, asNext ? NextInsertIndex() : -1);
        Library.RootChanged += root => _settings.MusicFolder = root;
        Library.EditCuesRequested += async path => await EditCues(path, Path.GetFileNameWithoutExtension(path));

        StreamToggle.Click += (_, _) => ToggleStreaming();
        // Right-click: explicit start and stop; Stop also clears an error (a click on an error retries).
        var startStream = new MenuItem { Header = "Start streaming" };
        startStream.Click += (_, _) => { if (_engine?.GetStatus().Stream is StreamState.Off or StreamState.Failed) ToggleStreaming(); };
        var stopStream = new MenuItem { Header = "Stop streaming" };
        stopStream.Click += (_, _) => _engine?.StopStreaming();
        var streamMenu = new ContextMenu { Items = { startStream, stopStream } };
        streamMenu.Opening += (_, _) =>
        {
            var state = _engine?.GetStatus().Stream ?? StreamState.Off;
            startStream.IsEnabled = state is StreamState.Off or StreamState.Failed;
            stopStream.IsEnabled = state != StreamState.Off;
            stopStream.Header = state switch
            {
                StreamState.Connecting => "Cancel connecting",
                StreamState.Failed => "Turn streaming off",
                _ => "Stop streaming",
            };
        };
        StreamToggle.ContextMenu = streamMenu;
        SettingsButton.Click += async (_, _) => await OpenSettings();

        // Tunnel: the operator's shortcuts come before whatever control has focus (Space on a button, keys in the playlist).
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShortcutKeyUp, RoutingStrategies.Tunnel);
        KeyDown += OnWindowKeyDown;
        // Releasing the mic key in another window never reaches us: close the mic when focus leaves.
        Deactivated += (_, _) =>
        {
            _keysDown.Clear();
            _micKeyHeld = false;
            UpdateMic();
        };
        Closing += (_, _) => SaveSettings();
        Closed += (_, _) => _engine?.Dispose();
    }

    /// <summary>Play resumes or continues; when stopped, it starts from the selected item if there is one.</summary>
    void PlayPressed()
    {
        bool stopped = _engine?.State == PlayerState.Stopped;
        _engine?.Play(stopped && Playlist.SelectedItem is PlaylistRow row ? row.Id : null);
    }

    static KeyModifiers CommandModifiers =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    /// <summary>The operator's shortcuts (Settings → Shortcuts): transport, mic and the carts on screen.</summary>
    void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (Shortcuts.FromEvent(e) is not { } pressed) return;
        // Plain keys type text in a text box (e.g. searching the music folder).
        if (FocusManager?.GetFocusedElement() is TextBox && Shortcuts.TypesText(pressed)) return;
        if (_shortcuts.FirstOrDefault(s => pressed.Equals(s.Gesture)).Id is not { } id) return;
        e.Handled = true;
        if (!_keysDown.Add(e.Key)) return; // auto-repeat
        switch (id)
        {
            case Shortcuts.Play: PlayPressed(); break;
            case Shortcuts.Pause: _engine?.TogglePause(); break;
            case Shortcuts.Next: _engine?.Next(); break;
            case Shortcuts.Stop: _engine?.Stop(); break;
            case Shortcuts.Mic: _micKeyHeld = true; UpdateMic(); break;
            case Shortcuts.Stream: ToggleStreaming(); break;
            default:
                ToggleCart(Array.FindIndex(Shortcuts.Actions, a => a.Id == id) - Array.FindIndex(Shortcuts.Actions, a => a.Id == Shortcuts.Cart(0)));
                break;
        }
    }

    void OnShortcutKeyUp(object? sender, KeyEventArgs e)
    {
        _keysDown.Remove(e.Key);
        // Key only: the modifiers may already be up.
        if (!_micKeyHeld || _shortcuts.FirstOrDefault(s => s.Id == Shortcuts.Mic).Gesture?.Key != e.Key) return;
        _micKeyHeld = false;
        UpdateMic();
        e.Handled = true;
    }

    /// <summary>Reads the shortcuts from the settings and shows them on the buttons.</summary>
    void ApplyShortcuts()
    {
        _shortcuts = _settings.Shortcuts
            .Select(kv => (Id: kv.Key, Gesture: Shortcuts.Parse(kv.Value)))
            .Where(s => s.Gesture != null && Shortcuts.Actions.Any(a => a.Id == s.Id))
            .ToList();
        string Key(string id) => _shortcuts.FirstOrDefault(s => s.Id == id).Gesture is { } g ? $" ({Shortcuts.Display(g)})" : "";
        ToolTip.SetTip(PlayButton, "Play" + Key(Shortcuts.Play));
        ToolTip.SetTip(PauseButton, "Pause / resume" + Key(Shortcuts.Pause));
        ToolTip.SetTip(NextButton, "Next, crossfade now" + Key(Shortcuts.Next));
        ToolTip.SetTip(StopButton, "Stop" + Key(Shortcuts.Stop));
        ToolTip.SetTip(MicButton, $"Hold to talk{Key(Shortcuts.Mic)}. Music and cartwall fade down while the mic is open.");
        ShowCartShortcuts();
        _lastStreamState = null; // refresh the STREAM tooltip
    }

    string StreamKey() =>
        _shortcuts.FirstOrDefault(s => s.Id == Shortcuts.Stream).Gesture is { } g ? $" ({Shortcuts.Display(g)})" : "";

    /// <summary>The shortcut for a cart, as shown on it ("" if none).</summary>
    string CartShortcut(int index) =>
        _shortcuts.FirstOrDefault(s => s.Id == Shortcuts.Cart(index)).Gesture is { } g ? Shortcuts.Display(g) : "";

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var cmd = CommandModifiers;
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers == cmd: _ = AddFilesFromPicker(); break;
            case Key.S when e.KeyModifiers == cmd: _ = SavePlaylist(); break;
            case Key.E when e.KeyModifiers == cmd: _ = EditSelectedCues(); break;
            case Key.Up when e.KeyModifiers == cmd: MoveSelected(-1); break;
            case Key.Down when e.KeyModifiers == cmd: MoveSelected(+1); break;
            case Key.OemComma when e.KeyModifiers == cmd: _ = OpenSettings(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Push to talk: the mic is open while the MIC button or its key is held (web radio only).</summary>
    void UpdateMic()
    {
        bool on = MicButton.IsVisible && (MicButton.IsPressed || _micKeyHeld);
        if (on == _micOn) return;
        _micOn = on;
        _engine?.SetMic(on);
    }

    // ───────────────────────── Polling: clock, meters, deck ─────────────────────────

    void Tick()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("HH:mm:ss");

        if (_engine == null) return;
        var st = _engine.GetStatus();

        MeterL.Push(st.LevelLeft);
        MeterR.Push(st.LevelRight);

        bool hasTrack = st.Current != null;
        NowTitle.Text = st.Current?.Title ?? "Nothing on air";
        NowArtist.Text = string.IsNullOrWhiteSpace(st.Current?.Artist) ? " " : st.Current!.Artist;
        UpdateWaveforms(st);
        // Times run from Cue In to Cue Out, not over the whole file.
        double start = st.Current?.Start ?? 0;
        double end = st.Current != null ? Math.Min(st.Current.End, st.Length) : 0;
        double elapsed = Math.Max(0, st.Position - start);
        if (st.Current is { } cur && _rowsById.TryGetValue(cur.Id, out var onAirRow))
            onAirRow.Progress = end > start ? elapsed / (end - start) : 0;
        Elapsed.Text = Format.Duration(elapsed);
        double remaining = Math.Max(0, end - st.Position);
        bool openEnded = st.Current?.IsOpenEnded == true;
        UpdateCueBadge(st, end);
        UpdateCueTimes(st);
        Remaining.Text = openEnded ? "∞" : "-" + Format.Duration(Math.Ceiling(remaining));
        Remaining.Foreground = hasTrack && !openEnded && remaining <= 10
            ? (IBrush)this.FindResource("OnAir")!
            : (IBrush)this.FindResource("TextMain")!;

        PlayButton.Classes.Set("active", st.State == PlayerState.Playing);
        PauseButton.Classes.Set("active", st.State == PlayerState.Paused);
        StopButton.Classes.Set("active", st.State == PlayerState.Stopped);
        MicButton.Classes.Set("live", _engine.MicLive);
        if (st.Current?.Id != _lastOnAirId)
        {
            // A new item went on air (Next button, double-click or auto-advance): flash Next.
            if (st.Current != null && _lastOnAirId != null) FlashNext();
            if (st.Current != null) CountPlayed();
            _lastOnAirId = st.Current?.Id;
        }

        (OnAirText.Text, OnAirBadge.Background) = st.State switch
        {
            PlayerState.Playing => ("ON AIR", (IBrush)this.FindResource("OnAir")!),
            PlayerState.Paused => ("PAUSED", (IBrush)this.FindResource("NextUp")!),
            _ => ("STOPPED", new SolidColorBrush(Color.Parse("#3A3F4B"))),
        };

        UpdateCarts();

        UpdateStreamStatus(st);

        // Once a second is plenty for the date, ETA column and station info.
        if (_slowTick-- > 0) return;
        _slowTick = 30;
        DateText.Text = now.ToString("dddd, d MMMM yyyy");
        UpdateStartTimes(st);
        UpdateStationInfo(now);
    }

    void UpdateWaveforms(PlayerStatus st)
    {
        if (st.Current?.Id != _waveTrackId)
        {
            _waveTrackId = st.Current?.Id;
            WaveMain.SetTrack(null, 0);
            WaveOverview.SetTrack(null, 0);
            if (st.Current is { } track && _engine != null)
            {
                var id = track.Id;
                double length = st.Length;
                _engine.LoadWaveformAsync(track.Path).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_waveTrackId != id) return; // track changed while decoding
                            WaveMain.SetTrack(t.Result, length);
                            WaveOverview.SetTrack(t.Result, length);
                        });
                });
            }
        }

        bool playing = st.State == PlayerState.Playing;
        var markers = st.Current is { } t ? CueMarkers.For(t, _engine?.CrossfadeSeconds ?? 0) : (CueMarkers?)null;
        WaveMain.SetMarkers(markers);
        WaveOverview.SetMarkers(markers);
        WaveMain.UpdatePosition(st.Position, playing);
        WaveOverview.UpdatePosition(st.Position, playing);
    }

    /// <summary>Talk-over countdown: time left in the intro, then (from the Outro marker) time left until Cue Out.</summary>
    void UpdateCueBadge(PlayerStatus st, double end)
    {
        string? label = null;
        double left = 0;
        if (st.State != PlayerState.Stopped && st.Current?.Cues is { } cues)
        {
            if (cues.Intro is { } intro && st.Position < intro) (label, left) = ("INTRO", intro - st.Position);
            else if (cues.Outro is { } outro && st.Position >= outro && st.Position < end) (label, left) = ("OUTRO", end - st.Position);
        }
        CueBadge.IsVisible = label != null;
        if (label == null) return;
        CueLabel.Text = label;
        CueTime.Text = Math.Ceiling(left).ToString("0");
        CueBadge.Classes.Set("intro", label == "INTRO");
        CueBadge.Classes.Set("outro", label == "OUTRO");
    }

    /// <summary>Time left until each cue point that is set (Intro, Outro, Mix); dimmed at zero once passed.</summary>
    void UpdateCueTimes(PlayerStatus st)
    {
        var cues = st.State != PlayerState.Stopped ? st.Current?.Cues : null;
        double? mix = cues?.Mix != null ? st.Current!.MixPoint(0) : null;
        Show(IntroInfo, IntroTime, cues?.Intro);
        Show(OutroInfo, OutroTime, cues?.Outro);
        Show(MixInfo, MixTime, mix);

        void Show(StackPanel info, TextBlock time, double? at)
        {
            info.IsVisible = at != null;
            if (at == null) return;
            double left = Math.Max(0, at.Value - st.Position);
            time.Text = Format.Duration(Math.Ceiling(left));
            info.Classes.Set("passed", left <= 0);
        }
    }

    void FlashNext()
    {
        NextButton.Classes.Set("flash", true);
        _nextFlash?.Dispose();
        _nextFlash = DispatcherTimer.RunOnce(() => NextButton.Classes.Set("flash", false), TimeSpan.FromMilliseconds(900));
    }

    void UpdateStartTimes(PlayerStatus st)
    {
        _playlistEnds = null;
        if (_snapshot == null) return;
        double xfade = _engine?.CrossfadeSeconds ?? 0;
        var items = _snapshot.Items;
        int current = _snapshot.CurrentId is { } cid ? items.ToList().FindIndex(t => t.Id == cid) : -1;
        int startFrom = _snapshot.NextId is { } nid ? items.ToList().FindIndex(t => t.Id == nid) : -1;

        // Mirrors the engine: each item starts at the previous one's mix point, and that one carries on to its Cue Out.
        double Tail(Track t) => t.End - t.MixPoint(xfade);
        double lastTail = st.Current is { } cur ? Tail(cur) : 0;

        // A stream that plays until Stop has no end, so nothing after it has a start time.
        DateTime? at = st.State == PlayerState.Playing && current >= 0 && st.Current is { IsOpenEnded: false }
            ? DateTime.Now.AddSeconds(Math.Max(0, st.Current.MixPoint(xfade) - st.Position))
            : null;

        for (int i = 0; i < items.Count; i++)
        {
            if (!_rowsById.TryGetValue(items[i].Id, out var row)) continue;
            if (at is { } t && startFrom >= 0 && i >= startFrom)
            {
                row.StartTime = t.ToString("HH:mm:ss");
                lastTail = Tail(items[i]);
                at = items[i].IsOpenEnded ? null : t.AddSeconds(items[i].PlayLength - lastTail);
            }
            else row.StartTime = "";
        }

        // When the last item finishes: the chain above stops at its mix point; with nothing queued it is the current item.
        if (at is { } end) _playlistEnds = end.AddSeconds(lastTail);
    }

    void UpdateStreamStatus(PlayerStatus st)
    {
        if (st.Stream == _lastStreamState) return;
        if (st.Stream == StreamState.OnAir) _streamSince = DateTime.Now;
        _lastStreamState = st.Stream;

        (StreamText.Text, StreamDot.Fill) = st.Stream switch
        {
            StreamState.OnAir => ("STREAMING", (IBrush)Brushes.White),
            StreamState.Connecting => ("CONNECTING…", (IBrush)this.FindResource("NextUp")!),
            StreamState.Failed => ("STREAM ERROR", (IBrush)this.FindResource("OnAir")!),
            _ => ("STREAM OFF", new SolidColorBrush(Color.Parse("#8B93A3"))),
        };
        StreamToggle.Classes.Set("onair", st.Stream == StreamState.OnAir);
        StreamToggle.Classes.Set("connecting", st.Stream == StreamState.Connecting);
        StreamToggle.Classes.Set("failed", st.Stream == StreamState.Failed);
        ToolTip.SetTip(StreamToggle, st.Stream switch
        {
            StreamState.OnAir => $"Streaming — click to stop{StreamKey()}",
            StreamState.Connecting => $"Connecting — click to cancel{StreamKey()}",
            StreamState.Failed => $"{st.StreamError}\nClick to retry, or right-click to turn streaming off",
            _ => $"Stream off — click to start (server is set in Settings){StreamKey()}",
        });
        _slowTick = 0;
    }

    /// <summary>The info block under the clock: top-of-hour countdown, playlist end, plays today, stream time.</summary>
    void UpdateStationInfo(DateTime now)
    {
        var toTop = TimeSpan.FromHours(1) - new TimeSpan(0, now.Minute, now.Second);
        TopOfHourText.Text = "-" + toTop.ToString(@"mm\:ss");

        PlaylistEndsText.Text = _playlistEnds?.ToString("HH:mm:ss") ?? "—";

        if (_playedDay != now.Date) { _playedDay = now.Date; _playedToday = 0; }
        PlayedTodayText.Text = _playedToday.ToString();

        StreamTimeText.Text = _lastStreamState == StreamState.OnAir
            ? Format.Duration((now - _streamSince).TotalSeconds)
            : "Off";
    }

    void CountPlayed()
    {
        if (_playedDay != DateTime.Today) { _playedDay = DateTime.Today; _playedToday = 0; }
        _playedToday++;
    }

    // ───────────────────────── Playlist ─────────────────────────

    void ApplySnapshot(PlaylistSnapshot snap)
    {
        _snapshot = snap;
        var selected = Playlist.SelectedItems?.OfType<PlaylistRow>().Select(r => r.Id).ToHashSet() ?? new();

        var desired = new List<PlaylistRow>(snap.Items.Count);
        var alive = new HashSet<Guid>();
        for (int i = 0; i < snap.Items.Count; i++)
        {
            var t = snap.Items[i];
            alive.Add(t.Id);
            if (!_rowsById.TryGetValue(t.Id, out var row)) _rowsById[t.Id] = row = new PlaylistRow(t);
            row.Track = t;
            row.Number = i + 1;
            row.IsPlaying = t.Id == snap.CurrentId;
            row.IsNext = t.Id == snap.NextId;
            row.IsPlayed = !row.IsPlaying && snap.Played.Contains(t.Id);
            desired.Add(row);
        }
        foreach (var dead in _rowsById.Keys.Where(k => !alive.Contains(k)).ToList()) _rowsById.Remove(dead);

        // In-place sync keeps the ListBox scroll position stable. Drop removed rows first: otherwise every row
        // after them gets moved up one by one, dragging the (selected) removed row to the bottom, and the list scrolls after it.
        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!alive.Contains(_rows[i].Id)) _rows.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i], desired[i])) continue;
            int existing = _rows.IndexOf(desired[i]);
            if (existing >= 0) _rows.Move(existing, i);
            else _rows.Insert(i, desired[i]);
        }
        while (_rows.Count > desired.Count) _rows.RemoveAt(_rows.Count - 1);

        foreach (var row in _rows.Where(r => selected.Contains(r.Id)))
            if (Playlist.SelectedItems?.Contains(row) == false) Playlist.SelectedItems.Add(row);

        EmptyHint.IsVisible = _rows.Count == 0;
        double total = snap.Items.Sum(t => t.PlayLength);
        PlaylistInfo.Text = _rows.Count == 0 ? "" : $"{_rows.Count} items · {Format.Duration(total)}";
        NextTitle.Text = snap.NextId is { } n && _rowsById.TryGetValue(n, out var next) ? next.Track.Display : "—";

        _settings.PlaylistItems = snap.Items.Select(PlaylistEntry.From).ToList();
    }

    void RemoveSelected()
    {
        foreach (var row in Playlist.SelectedItems?.OfType<PlaylistRow>().ToList() ?? new())
            _engine?.Remove(row.Id);
    }

    void MoveSelected(int delta)
    {
        var selected = Playlist.SelectedItems?.OfType<PlaylistRow>().Select(r => _rows.IndexOf(r)).Where(i => i >= 0).Order().ToList();
        if (selected is not { Count: > 0 }) return;
        int insertAt = delta < 0 ? selected[0] - 1 : selected[^1] + 2;
        if (insertAt < 0 || insertAt > _rows.Count) return;
        _engine?.Move(selected.Select(i => _rows[i].Id).ToList(), insertAt);
    }

    /// <summary>Playlist row under the pointer, or null over empty space.</summary>
    ListBoxItem? RowUnder(DragEventArgs e) =>
        (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);

    /// <summary>Insertion index for a drop: before the row under the pointer, or after it on its lower half.</summary>
    int DropIndex(DragEventArgs e)
    {
        if (RowUnder(e) is not { } item) return _rows.Count;
        int index = Playlist.IndexFromContainer(item);
        if (index < 0) return _rows.Count;
        return e.GetPosition(item).Y > item.Bounds.Height / 2 ? index + 1 : index;
    }

    void ShowDropIndicator(DragEventArgs e)
    {
        int index = DropIndex(e);
        // Draw the line at the top of the row it goes before, or under the last row.
        var anchor = index < _rows.Count ? Playlist.ContainerFromIndex(index) : _rows.Count > 0 ? Playlist.ContainerFromIndex(_rows.Count - 1) : null;
        if (anchor?.TranslatePoint(new Point(0, index < _rows.Count ? 0 : anchor.Bounds.Height), DropLayer) is not { } pt)
        {
            DropIndicator.IsVisible = false;
            return;
        }
        Canvas.SetLeft(DropIndicator, 4);
        Canvas.SetTop(DropIndicator, Math.Max(0, pt.Y - 1.5));
        DropIndicator.Width = Math.Max(0, DropLayer.Bounds.Width - 8);
        DropIndicator.IsVisible = true;

        // Auto-scroll when dragging near the top or bottom edge.
        if (Playlist.FindDescendantOfType<ScrollViewer>() is { } sv)
        {
            double y = e.GetPosition(Playlist).Y;
            if (y < 28) sv.Offset = sv.Offset.WithY(Math.Max(0, sv.Offset.Y - 18));
            else if (y > Playlist.Bounds.Height - 28) sv.Offset = sv.Offset.WithY(sv.Offset.Y + 18);
        }
    }

    async Task AddFilesFromPicker()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add audio files",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = AudioFiles.Patterns }, FilePickerFileTypes.All],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) _engine?.AddFiles(paths);
    }

    async Task OpenPlaylist()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open playlist",
            FileTypeFilter = [new FilePickerFileType("M3U playlist") { Patterns = ["*.m3u", "*.m3u8"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        var baseDir = Path.GetDirectoryName(path)!;
        var entries = new List<PlaylistEntry>();
        (double Seconds, string Name)? info = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // #EXTINF:<seconds>,<name>; for a stream, -1 (or 0) means until Stop or Next.
                var parts = line["#EXTINF:".Length..].Split(',', 2);
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secs);
                info = (secs, parts.Length > 1 ? parts[1].Trim() : "");
                continue;
            }
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (PlayoutEngine.IsUrl(line))
                entries.Add(new PlaylistEntry
                {
                    Path = line,
                    StreamName = info is { Name.Length: > 0 } i ? i.Name : line,
                    StreamSeconds = Math.Max(0, info?.Seconds ?? 0),
                });
            else
                entries.Add(new PlaylistEntry { Path = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(baseDir, line)) });
            info = null;
        }
        AddEntries(entries);
    }

    /// <summary>Adds saved or loaded playlist items in order: files that still exist, and streams.</summary>
    void AddEntries(IEnumerable<PlaylistEntry> entries)
    {
        if (_engine is not { } engine) return;
        var files = new List<string>();
        foreach (var e in entries)
        {
            if (!e.IsStream)
            {
                if (File.Exists(e.Path)) files.Add(e.Path);
                continue;
            }
            Flush();
            engine.AddStream(e.Path, e.StreamName!, e.StreamSeconds);
        }
        Flush();

        // The engine runs commands in order, so batching the files between streams keeps the order.
        void Flush()
        {
            if (files.Count == 0) return;
            engine.AddFiles(files.ToList());
            files.Clear();
        }
    }

    async Task AddStream()
    {
        if (_engine == null) return;
        if (await new StreamDialog().ShowDialog<StreamRequest?>(this) is not { } s) return;
        _engine.AddStream(s.Url, s.Name, s.Seconds, s.PlayNext ? NextInsertIndex() : -1);
    }

    async Task SavePlaylist()
    {
        if (_snapshot == null || _snapshot.Items.Count == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save playlist",
            DefaultExtension = "m3u8",
            SuggestedFileName = $"playlist-{DateTime.Now:yyyyMMdd-HHmm}.m3u8",
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        var lines = new List<string> { "#EXTM3U" };
        foreach (var t in _snapshot.Items)
        {
            lines.Add($"#EXTINF:{(t.IsOpenEnded ? -1 : (int)t.Duration)},{t.Display}");
            lines.Add(t.Path);
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>Adds files and folders (expanded recursively, off the UI thread) at a playlist position; -1 appends.</summary>
    void AddPaths(IReadOnlyList<string> paths, int insertAt)
    {
        if (_engine is not { } engine) return;
        Task.Run(() =>
        {
            var files = AudioFiles.Expand(paths).ToList();
            if (files.Count > 0) engine.AddFiles(files, insertAt);
        });
    }

    /// <summary>Position right after the on-air item, so "Play next" items go ahead of the rest.</summary>
    int NextInsertIndex()
    {
        if (_snapshot?.CurrentId is not { } id) return _snapshot?.NextId is { } n ? _rows.IndexOf(_rowsById[n]) : -1;
        return _rowsById.TryGetValue(id, out var row) ? _rows.IndexOf(row) + 1 : -1;
    }

    // ───────────────────────── Cue points ─────────────────────────

    async Task EditSelectedCues()
    {
        if (Playlist.SelectedItem is PlaylistRow { Track.IsStream: false } row) await EditCues(row.Track.Path, row.Track.Display);
    }

    bool _cueEditorOpen;

    async Task EditCues(string path, string title)
    {
        if (_engine == null || _cueEditorOpen) return;
        _cueEditorOpen = true;
        try
        {
            await new CueEditorWindow(_engine, path, title).ShowDialog(this);
        }
        finally
        {
            _cueEditorOpen = false;
        }
    }

    // ───────────────────────── Streaming ─────────────────────────

    void ToggleStreaming()
    {
        if (_engine == null) return;
        if (_engine.GetStatus().Stream is StreamState.OnAir or StreamState.Connecting)
        {
            _engine.StopStreaming();
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.StreamHost) || _settings.StreamPort <= 0)
        {
            LastLog.Text = "Set up the streaming server in Settings first";
            _ = OpenSettings();
            return;
        }

        int[] bitrates = [64, 96, 128, 192, 256, 320];
        _engine.StartStreaming(new StreamSettings(
            _settings.StreamHost, _settings.StreamPort, _settings.StreamMount, _settings.StreamPassword,
            _settings.StreamFormat == 1 ? StreamFormat.Ogg : StreamFormat.Mp3,
            bitrates[Math.Clamp(_settings.StreamBitrateIndex, 0, bitrates.Length - 1)],
            StationName: string.IsNullOrWhiteSpace(_settings.StationName) ? "Radio Playout" : _settings.StationName,
            Genre: _settings.StationGenre,
            Shoutcast: _settings.Shoutcast,
            Url: string.IsNullOrWhiteSpace(_settings.StationUrl) ? null : _settings.StationUrl));
    }

    // ───────────────────────── Settings ─────────────────────────

    async Task OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        try
        {
            var routingBefore = _settings.Routing;
            var micBefore = _settings.ActiveMic;
            bool saved = await new SettingsWindow(_settings, _engine).ShowDialog<bool>(this);
            if (!saved) return;
            if (_engine != null)
            {
                _engine.CrossfadeSeconds = _settings.Crossfade;
                if (_settings.Routing != routingBefore) _engine.ApplyRouting(_settings.Routing);
                if (_settings.ActiveMic != micBefore) _engine.ConfigureMic(_settings.ActiveMic);
                _engine.ConfigureTimeSignal(_settings.TimeSignal);
            }
            ApplyStationBranding();
            ShowStationType();
            ApplyShortcuts();
            LastLog.Text = "Settings saved";
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    void ApplyStationBranding()
    {
        bool hasName = !string.IsNullOrWhiteSpace(_settings.StationName);
        StationNameText.Text = hasName ? _settings.StationName.ToUpperInvariant() : "";
        StationNameText.IsVisible = hasName;
        Title = hasName ? $"{_settings.StationName} — Radio Playout" : "Radio Playout";

        (LogoImage.Source as IDisposable)?.Dispose();
        LogoImage.Source = null;
        if (_settings.LogoPath is { } path && File.Exists(path))
        {
            try { LogoImage.Source = new Bitmap(path); }
            catch { /* unreadable image: just show no logo */ }
        }
        LogoFrame.IsVisible = LogoImage.Source != null;
    }

    /// <summary>AUTO runs through the playlist; ASSISTED stops after each item until the operator starts the next.</summary>
    void ShowAutomationMode()
    {
        bool auto = AutoSwitch.IsChecked == true;
        AutoContent.IsVisible = auto;
        AssistedContent.IsVisible = !auto;
        ToolTip.SetTip(AutoSwitch, auto
            ? "AUTO: plays through the playlist with crossfades. Click for ASSISTED."
            : "ASSISTED: stops after each item; start the next one yourself. Click for AUTO.");
    }

    /// <summary>Web radio gets a MIC button; on FM the mic goes through the desk.</summary>
    void ShowStationType()
    {
        MicButton.IsVisible = _settings.StationType == StationType.Web;
        UpdateMic();
    }

    void LoadSettingsIntoUi()
    {
        ShowStationType();
        AutoSwitch.IsChecked = _settings.AutoAdvance;
        ShowAutomationMode();
        Library.SetRoot(_settings.MusicFolder);
        ApplyStationBranding();
    }

    void SaveSettings() => _settings.Save();
}
