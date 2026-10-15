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
            _engine = new PlayoutEngine(_settings.Routing, cues, ForcedDeviceFromArgs(),
                new LoudnessStore(Path.Combine(AppSettings.DataFolder, "loudness.txt")));
            _engine.Log += msg =>
            {
                AppLog.Write(msg);
                Dispatcher.UIThread.Post(() => LastLog.Text = msg);
            };
            Watchdog.Watch(_engine);
            _engine.OnAir += track => _library.RecordPlay(track, "playlist");
            _engine.OnAir += track => _asRun.Started(track, DateTime.UtcNow);
            _engine.OffAir += (track, started, ended) => _asRun.Ended(track, started, ended);
            var engine = _engine;
            _library.AttachCues(cues, paths => engine.ReloadCues(paths));
            _engine.PlaylistChanged += snap => Dispatcher.UIThread.Post(() => ApplySnapshot(snap));
        }
        catch (Exception ex)
        {
            LastLog.Text = $"Audio engine failed to start: {ex.Message}";
            LastLog.Foreground = (IBrush)this.FindResource("OnAir")!;
        }

        _library.Message += message =>
        {
            AppLog.Write(message);
            LastLog.Text = message;
        };
        _library.Changed += () => ScanButton.Content = _library.IsScanning ? "Stop" : "Scan";
        Tracks.Attach(_library);
        _ = _library.OpenAsync(_settings.Library);
        ApplyAutoScan();

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
            ApplySafety();
            ApplyRds();
            ApplyLoudness();
            AddEntries(_settings.PlaylistItems);
            _ = CarryOn();
            _scheduler = new Scheduler(_engine, ScheduleData.Load());
            _scheduler.Message += message =>
            {
                AppLog.Write(message);
                Dispatcher.UIThread.Post(() => LastLog.Text = message);
            };
        }
        ApplyWebNowPlaying();
        AppLog.Write($"Started {AppInfo.Name} {Updates.CurrentText}");

        // Saved every few seconds (only when something changed), so a crash or power cut loses next to nothing.
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => SaveSettings());
        _saveTimer.Start();

        _fastTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        _fastTimer.Start();

        // Look for a new version shortly after start (not while everything is still loading), then every few hours.
        _updateTimer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, async (_, _) =>
        {
            _updateTimer!.Interval = UpdateCheckInterval;
            if (_settings.CheckForUpdates && !Updates.IsDevBuild) await CheckForUpdate(quiet: true);
        });
        _updateTimer.Start();
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
        var editTags = new MenuItem { Header = "Edit tags…", InputGesture = new KeyGesture(Key.T, CommandModifiers) };
        editTags.Click += async (_, _) => await EditSelectedTags();
        var remove = new MenuItem { Header = "Remove", InputGesture = new KeyGesture(Key.Delete) };
        remove.Click += (_, _) => RemoveSelected();
        Playlist.ContextMenu = new ContextMenu { Items = { editCues, editTags, remove } };

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
        Tracks.AddRequested += (paths, asNext) => AddPaths(paths, asNext ? NextInsertIndex() : -1);
        Tracks.EditCuesRequested += async path => await EditCues(path, Path.GetFileNameWithoutExtension(path));
        Tracks.EditTagsRequested += async path => await EditTags(path);
        // Clicking the open tab keeps it open (ShowMusicTab sets both buttons again).
        LibraryTab.Click += (_, _) => ShowMusicTab(true);
        FoldersTab.Click += (_, _) => ShowMusicTab(false);
        ScanButton.Click += async (_, _) =>
        {
            if (_library.IsScanning) _library.CancelScan();
            else await _library.ScanAsync(_settings.Library.Folders);
        };
        Library.RootChanged += root => _settings.MusicFolder = root;
        Library.EditCuesRequested += async path => await EditCues(path, Path.GetFileNameWithoutExtension(path));
        Library.EditTagsRequested += async path => await EditTags(path);

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
        ScheduleButton.Click += async (_, _) => await OpenSchedule();
        ReportsButton.Click += (_, _) => OpenReports();

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
        Closed += (_, _) => _rds?.Dispose();
        Closed += (_, _) => _web?.Dispose();
        Closed += (_, _) =>
        {
            _scheduler?.Dispose();
            LogCartsOnAir();
            _asRun.Close();
        };
        Closed += (_, _) =>
        {
            _engine?.Dispose();
            // After the settings are saved and the sound card is free, so the new version starts cleanly.
            if (_restartAfterClose) Updates.Restart();
        };
        UpdateButton.Click += async (_, _) => { if (_update != null) await ShowUpdate(_update); };
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
            case Shortcuts.Stream: if (StreamToggle.IsVisible) ToggleStreaming(); break;
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
            case Key.T when e.KeyModifiers == cmd: _ = EditSelectedTags(); break;
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
        _rds?.Update(st.State == PlayerState.Stopped ? null : st.Current);

        MeterL.Push(st.LevelLeft);
        MeterR.Push(st.LevelRight);

        bool hasTrack = st.Current != null;
        NowTitle.Text = st.Current?.Title ?? "Nothing on air";
        NowArtist.Text = string.IsNullOrWhiteSpace(st.Current?.Artist) ? " " : st.Current!.Artist;
        UpdateWaveforms(st);
        ShowCover(st.Current);
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
        ShowSilence(st);
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
        _web?.Update(st.State == PlayerState.Stopped ? null : st.Current, NextItem());
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
            // An item with a latest start starts then at the latest (what is on air is faded).
            if (at is { } late && startFrom >= 0 && i >= startFrom && items[i].StartBy is { } by && by.ToLocalTime() < late) at = by.ToLocalTime();
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
        ShowStreaming();

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

        var next = _scheduler?.Next(now);
        NextEventPanel.IsVisible = next != null;
        if (next is { } n)
            NextEventText.Text = $"{n.At:HH:mm:ss}  {(string.IsNullOrWhiteSpace(n.Name) ? "Event" : n.Name)}"
                                 + (n.At.Date != now.Date ? " (tomorrow)" : "");
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

        // Not while the saved playlist is still being added: a save then would lose the rest of it.
        if (_restored) _settings.PlaylistItems = snap.Items.Select(PlaylistEntry.From).ToList();
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
            if (e.Event != null)
            {
                if (!e.IsStream && !File.Exists(e.Path)) continue;
                Flush();
                engine.AddScheduled(e.Event, [e.ToScheduled()]);
                continue;
            }
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

    bool _editorOpen;

    async Task EditCues(string path, string title)
    {
        if (_engine == null || _editorOpen) return;
        _editorOpen = true;
        try
        {
            await new CueEditorWindow(_engine, path, title).ShowDialog(this);
        }
        finally
        {
            _editorOpen = false;
        }
    }

    // ───────────────────────── Tags ─────────────────────────

    string? _coverPath; // file whose cover is shown (or being loaded)

    /// <summary>Shows the album cover of the item on air, if its file has one.</summary>
    void ShowCover(Track? track)
    {
        string? path = track is { IsStream: false } ? track.Path : null;
        if (path == _coverPath) return;
        _coverPath = path;
        if (path == null)
        {
            SetCover(null);
            return;
        }
        Task.Run(() =>
        {
            if (Tags.ReadCover(path) is not { } data) return null;
            try { return Bitmap.DecodeToWidth(new MemoryStream(data), 328); }
            catch { return null; } // not a picture we can show
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (path == _coverPath) SetCover(t.Result);
            else t.Result?.Dispose(); // another item is on air by now
        }));
    }

    void SetCover(Bitmap? cover)
    {
        (NowCover.Source as IDisposable)?.Dispose();
        NowCover.Source = cover;
        NowCoverFrame.IsVisible = cover != null;
    }

    async Task EditSelectedTags()
    {
        if (Playlist.SelectedItem is PlaylistRow { Track.IsStream: false } row) await EditTags(row.Track.Path);
    }

    async Task EditTags(string path)
    {
        if (_engine == null || _editorOpen) return;
        _editorOpen = true;
        try
        {
            if (await new TagEditorWindow(_engine, path).ShowDialog<bool>(this)) _library.Refresh(path);
        }
        finally
        {
            _editorOpen = false;
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
            StationName: string.IsNullOrWhiteSpace(_settings.StationName) ? AppInfo.Name : _settings.StationName,
            Genre: _settings.StationGenre,
            Shoutcast: _settings.Shoutcast,
            Url: string.IsNullOrWhiteSpace(_settings.StationUrl) ? null : _settings.StationUrl));
    }

    // ───────────────────────── Settings ─────────────────────────

    // ───────────────────────── Updates ─────────────────────────

    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    DispatcherTimer? _updateTimer;
    UpdateInfo? _update;
    bool _restartAfterClose;

    /// <summary>Asks GitHub for a newer version; shows the update button if there is one. Quiet: no message unless one is found.</summary>
    public async Task<UpdateInfo?> CheckForUpdate(bool quiet)
    {
        try
        {
            _update = await Updates.CheckAsync();
            if (_update != null)
            {
                UpdateButton.Content = $"Update {_update.Version} available";
                ToolTip.SetTip(UpdateButton, $"{AppInfo.Name} {_update.Version} is out (you have {Updates.CurrentText}). Click for details.");
                if (!quiet || !UpdateButton.IsVisible) LastLog.Text = $"{AppInfo.Name} {_update.Version} is available";
            }
            UpdateButton.IsVisible = _update != null;
            return _update;
        }
        catch (Exception ex)
        {
            if (!quiet) throw;
            System.Diagnostics.Trace.WriteLine($"Update check failed: {ex.Message}"); // offline etc.: try again next time
            return null;
        }
    }

    public async Task ShowUpdate(UpdateInfo update, Window? owner = null)
    {
        bool close = await new UpdateWindow(update, _engine).ShowDialog<bool>(owner ?? this);
        if (!close) return;
        _restartAfterClose = !Updates.InstallClosesApp; // Windows: the installer starts the new version
        (owner as Window)?.Close();
        Close();
    }

    async Task OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        try
        {
            var routingBefore = _settings.Routing;
            string libraryBefore = _settings.Library.ConnectionKey();
            var micBefore = _settings.ActiveMic;
            bool saved = await new SettingsWindow(_settings, _engine, this).ShowDialog<bool>(this);
            if (!saved) return;
            if (_engine != null)
            {
                _engine.CrossfadeSeconds = _settings.Crossfade;
                if (_settings.Routing != routingBefore) _engine.ApplyRouting(_settings.Routing);
                if (_settings.ActiveMic != micBefore) _engine.ConfigureMic(_settings.ActiveMic);
                _engine.ConfigureTimeSignal(_settings.TimeSignal);
                ApplySafety();
                ApplyRds();
                ApplyLoudness();
            }
            ApplyWebNowPlaying();
            if (_settings.Library.ConnectionKey() != libraryBefore) _ = _library.OpenAsync(_settings.Library);
            ApplyAutoScan();
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
        Title = hasName ? $"{_settings.StationName} — {AppInfo.Name}" : AppInfo.Name;

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
        ShowStreaming();
    }

    /// <summary>
    /// The STREAM button and stream time show on web radio or once a streaming server is set up, and while a stream
    /// is running (so it can be stopped).
    /// </summary>
    void ShowStreaming()
    {
        bool stream = _settings.UsesStreaming || _lastStreamState is { } s && s != StreamState.Off;
        StreamToggle.IsVisible = stream;
        StreamTimePanel.IsVisible = stream;
    }

    void LoadSettingsIntoUi()
    {
        ShowStationType();
        AutoSwitch.IsChecked = _settings.AutoAdvance;
        ShowAutomationMode();
        Library.SetRoot(_settings.MusicFolder);
        ApplyStationBranding();
    }

    void SaveSettings()
    {
        if (_engine != null && _restored && _snapshot is { } snap)
        {
            // What was on air (else the last item played), for carrying on after a restart.
            var items = snap.Items;
            int onAir = -1;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Id == snap.CurrentId) { onAir = i; break; }
                else if (snap.Played.Contains(items[i].Id)) onAir = i;
            _settings.OnAirIndex = onAir;
            _settings.WasPlaying = _engine.State == PlayerState.Playing && _engine.AutoAdvance;
            _settings.StreamingOn = _engine.GetStatus().Stream != StreamState.Off;
        }
        _settings.Save();
    }

    // ───────────────────────── Library ─────────────────────────

    readonly LibraryService _library = new();

    /// <summary>The music library (for the settings window).</summary>
    internal LibraryService MusicLibrary => _library;

    void ApplyAutoScan()
    {
        var library = _settings.Library;
        _library.ConfigureAutoScan(library.Folders, library.ScanEveryMinutes, library.WatchFolders);
    }

    /// <summary>The library (search) or the folders on disk in the left panel.</summary>
    void ShowMusicTab(bool library)
    {
        LibraryTab.IsChecked = library;
        FoldersTab.IsChecked = !library;
        Tracks.IsVisible = library;
        Library.IsVisible = !library;
        ScanButton.IsVisible = library;
    }

    // ───────────────────────── Safety ─────────────────────────

    readonly DispatcherTimer _saveTimer;
    bool _restored; // the saved playlist is back in the engine: from now on the playlist is saved

    /// <summary>
    /// Puts back the saved playlist position and, if AUTO was playing when the app closed, crashed or the computer
    /// went down, carries on with the next item. Streaming is switched back on too.
    /// </summary>
    async Task CarryOn()
    {
        // Missing files are not added again: count what's left up to the item that was on air.
        int index = _settings.PlaylistItems.Take(_settings.OnAirIndex + 1).Count(e => e.IsStream || File.Exists(e.Path)) - 1;
        bool play = _settings.ResumeOnStart && _settings.WasPlaying && _settings.AutoAdvance;
        await _engine!.Resume(index, play);
        _restored = true;
        if (_settings.ResumeOnStart && _settings.StreamingOn && _settings.UsesStreaming && !string.IsNullOrWhiteSpace(_settings.StreamHost)) ToggleStreaming();
    }

    /// <summary>Dead air detection and fill music, from the settings.</summary>
    void ApplySafety()
    {
        if (_engine == null) return;
        _engine.ConfigureSilence(_settings.Silence);
        Func<string?>? fill = null;
        if (_settings.FillEnabled)
        {
            var folder = Directory.Exists(_settings.FillFolder) ? new FillMusic(_settings.FillFolder) : null;
            fill = _settings.FillSource == FillSource.Library
                ? new LibraryFill(_library, _settings.FillCategories, TimeSpan.FromHours(_settings.FillSongHours),
                    TimeSpan.FromMinutes(_settings.FillArtistMinutes), folder).Next
                : folder == null ? null : folder.Next;
        }
        _engine.ConfigureFill(fill);
    }

    // ───────────────────────── RDS ─────────────────────────

    RdsOutput? _rds;

    /// <summary>Starts (or restarts, with new settings) sending now playing to the RDS encoder.</summary>
    void ApplyRds()
    {
        _rds?.Dispose();
        _rds = null;
        if (_settings.Rds is not { Enabled: true } settings) return;
        string station = string.IsNullOrWhiteSpace(_settings.StationName) ? AppInfo.Name : _settings.StationName;
        var rds = new RdsOutput(settings, station);
        rds.StatusChanged += error => Dispatcher.UIThread.Post(() =>
        {
            if (rds != _rds) return;
            string message = error == null ? "RDS: sending to the encoder" : $"RDS: can't send to the encoder ({error}); trying again";
            AppLog.Write(message);
            LastLog.Text = message;
        });
        _rds = rds;
    }

    // ───────────────────────── Schedule, reports, website ─────────────────────────

    readonly AsRunLog _asRun = new(AsRunLog.DefaultFolder);
    Scheduler? _scheduler;
    WebNowPlaying? _web;
    bool _scheduleOpen;
    ReportsWindow? _reports;

    async Task OpenSchedule()
    {
        if (_scheduler == null || _scheduleOpen) return;
        _scheduleOpen = true;
        try
        {
            if (await new ScheduleWindow(_scheduler.Current, _settings.Library.Folders.FirstOrDefault() ?? _settings.MusicFolder)
                    .ShowDialog<ScheduleData?>(this) is { } edited)
            {
                _scheduler.Update(edited);
                LastLog.Text = "Schedule saved";
            }
        }
        finally
        {
            _scheduleOpen = false;
        }
    }

    void OpenReports()
    {
        if (_reports != null) { _reports.Activate(); return; }
        _reports = new ReportsWindow(AsRunLog.DefaultFolder, _settings.StationName);
        _reports.Closed += (_, _) => _reports = null;
        _reports.Show(this);
    }

    /// <summary>The item after the one on air, for the website.</summary>
    Track? NextItem() =>
        _snapshot?.NextId is { } id && _rowsById.TryGetValue(id, out var row) ? row.Track : null;

    /// <summary>Starts (or restarts, with new settings) posting now playing to the website.</summary>
    void ApplyWebNowPlaying()
    {
        _web?.Dispose();
        _web = null;
        if (_settings.WebNowPlaying is not { Enabled: true } settings) return;
        string station = string.IsNullOrWhiteSpace(_settings.StationName) ? AppInfo.Name : _settings.StationName;
        var web = new WebNowPlaying(settings, station);
        web.StatusChanged += error => Dispatcher.UIThread.Post(() =>
        {
            if (web != _web) return;
            string message = error == null ? "Website: now playing sent" : $"Website: can't send now playing ({error}); trying again";
            AppLog.Write(message);
            LastLog.Text = message;
        });
        _web = web;
    }

    /// <summary>Loudness levelling from the settings; the carts are measured ahead (the playlist is measured by the engine).</summary>
    void ApplyLoudness()
    {
        if (_engine == null) return;
        _engine.ConfigureLoudness(_settings.Loudness);
        _engine.MeasureLoudness(_settings.Cartwalls.SelectMany(c => c.Carts).Select(c => c.Path).OfType<string>().Where(File.Exists).Distinct().ToList());
    }

    void ShowSilence(PlayerStatus st)
    {
        bool silent = st.Silence >= 5;
        SilenceBadge.IsVisible = silent;
        if (silent)
        {
            SilenceTime.Text = Format.Duration(st.Silence);
            CueBadge.IsVisible = false;
        }
    }
}
