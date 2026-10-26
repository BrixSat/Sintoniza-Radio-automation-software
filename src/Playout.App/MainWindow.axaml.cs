using Playout.Library;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
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
        Localization.Apply(this);
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
            _engine.PlaylistChanged += snap => Dispatcher.UIThread.Post(() =>
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                ApplySnapshot(snap);
                Slow("playlist", start);
            });
        }
        catch (Exception ex)
        {
            LastLog.Text = L.F("Audio engine failed to start: {0}", ex.Message);
            LastLog.Foreground = (IBrush)this.FindResource("OnAir")!;
        }

        _library.Message += message =>
        {
            AppLog.Write(message);
            LastLog.Text = message;
        };
        _library.Changed += () => ScanButton.Content = _library.IsScanning ? L.T("Stop") : L.T("Scan");
        Tracks.Attach(_library);
        Tracks.CategoryFolders = () => CategoryFolders();
        Tracks.CanEditCategories = false; // Sintoniza Pro edits them
        _library.Changed += MoveFolderCategories;
        _asRun.Database = () => _library.Db;
        _library.Changed += () => _asRun.Sync(now: true); // the history in the files, once the database is open
        _ = _library.OpenAsync(_settings.Library);
        ApplyAutoScan();

        BuildCartwall();
        LoadSettingsIntoUi();
        ApplyShortcuts();
        WireEvents();
        WirePlaylistView();
        WireLogin();

        if (_engine != null)
        {
            _engine.CrossfadeSeconds = _settings.Crossfade;
            _engine.OtherCrossfadeSeconds = _settings.OtherCrossfade;
            _engine.AutoAdvance = _settings.AutoAdvance;
            _engine.ConfigureMic(_settings.ActiveMic);
            _engine.ConfigureTimeSignal(_settings.TimeSignal);
            // Takes: the time and temperature, put together when they go on air
            _takes.Settings = _settings.Takes ?? new();
            _takes.Message += message => AppLog.Write(message);
            _engine.TakeSource = _takes.Source;
            ApplySafety();
            ApplyRds();
            ApplyGpio();
            ApplyLoudness();
            FillPlayedTimes(_settings.PlaylistItems, _settings.OnAirIndex);
            AddEntries(_settings.PlaylistItems);
            _ = CarryOn();
            _scheduler = new Scheduler(_engine, ScheduleSource.LoadLocal(), new ScheduleSource(() => _library.Db));
            _scheduler.Message += message =>
            {
                AppLog.Write(message);
                Dispatcher.UIThread.Post(() => LastLog.Text = message);
            };
            // Saved in the schedule program: show what is coming up now.
            _scheduler.Reloaded += () => Dispatcher.UIThread.Post(() => ReadUpcoming(DateTime.Now, force: true));
            var scheduler = _scheduler;
            // The daily playlists: made ahead, put in the playlist as their hours come.
            _plan = new PlanPlayer(_engine, _library, () => scheduler.Current, PlanOptions, () => Math.Clamp(_settings.PlanDaysAhead, 0, 14));
            _scheduler.Reloaded += () => _plan.ScheduleChanged();
            _plan.Message += message =>
            {
                AppLog.Write(message);
                Dispatcher.UIThread.Post(() => LastLog.Text = message);
            };
            ApplySafety();
        }
        ApplyWebNowPlaying();
        ApplyRemote();
        StartBackups();
        AppLog.Write($"Started {AppInfo.Name} {Updates.CurrentText}");

        // Saved every few seconds (only when something changed), so a crash or power cut loses next to nothing.
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => SaveSettings());
        _saveTimer.Start();

        _fastTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        _fastTimer.Start();
        // How quickly the window answers, from outside it: a stall over a second goes in the log (to find why).
        _responseProbe = new Timer(_ =>
        {
            long asked = System.Diagnostics.Stopwatch.GetTimestamp();
            Dispatcher.UIThread.Post(() =>
            {
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalMilliseconds;
                long now = Environment.TickCount64;
                if (ms >= 1000 && now - _lastStallLogged > 30_000)
                {
                    _lastStallLogged = now;
                    AppLog.Write($"The window didn't respond for {ms / 1000:0.0} s");
                }
            }, DispatcherPriority.Input);
        }, null, 5000, 1000);

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
    internal static int? ForcedDeviceFromArgs()
    {
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--device="));
        return arg != null && int.TryParse(arg["--device=".Length..], out int d) ? d : null;
    }

    // ───────────────────────── Wiring ─────────────────────────

    void WireEvents()
    {
        PlayButton.Click += (_, _) => PlayPressed();
        PauseButton.Click += async (_, _) => await PausePressed();
        NextButton.Click += async (_, _) => await NextPressed();
        StopButton.Click += async (_, _) => await StopPressed();
        MicButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == Button.IsPressedProperty) UpdateMic();
        };
        MicButton.Click += (_, _) => ToggleMic();

        AutoSwitch.IsCheckedChanged += (_, _) =>
        {
            _settings.AutoAdvance = AutoSwitch.IsChecked == true;
            if (_engine != null) _engine.AutoAdvance = _settings.AutoAdvance;
            ShowAutomationMode();
        };

        AddStreamButton.Click += async (_, _) => await AddStream();
        OpenListButton.Click += async (_, _) => await OpenPlaylist();
        SaveListButton.Click += async (_, _) => await SavePlaylist();
        RemoveButton.Click += (_, _) => RemoveSelected();
        ClearButton.Click += (_, _) => _engine?.Clear();
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton.Click += (_, _) => MoveSelected(+1);

        Playlist.DoubleTapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is PlaylistRow row) _ = PlayNow(row);
        };
        var editCues = new MenuItem { Header = L.T("Edit cue points…"), InputGesture = new KeyGesture(Key.E, CommandModifiers) };
        editCues.Click += async (_, _) => await EditSelectedCues();
        var editSegue = new MenuItem { Header = L.T("Segue with the next…"), InputGesture = new KeyGesture(Key.G, CommandModifiers) };
        editSegue.Click += async (_, _) => await EditSelectedSegue();
        var editTags = new MenuItem { Header = L.T("Edit tags…"), InputGesture = new KeyGesture(Key.T, CommandModifiers) };
        editTags.Click += async (_, _) => await EditSelectedTags();
        var remove = new MenuItem { Header = L.T("Remove"), InputGesture = new KeyGesture(Key.Delete) };
        remove.Click += (_, _) => RemoveSelected();
        Playlist.ContextMenu = new ContextMenu { Items = { editCues, editSegue, editTags, remove } };

        Playlist.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter && Playlist.SelectedItem is PlaylistRow row) { _ = PlayNow(row); e.Handled = true; }
        };

        // Rows can be dragged to reorder; drops also come from the music browser or the OS file manager.
        DragHelper.Attach(Playlist,
            () => Playlist.SelectedItems?.OfType<PlaylistRow>().ToList() ?? new(),
            rows => DragHelper.Data(RowDragFormat, rows.Select(r => r.Id).ToArray(), string.Join("\n", rows.Select(r => r.Track.Display))),
            DragDropEffects.Move);

        Playlist.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(RowDragFormat) ? DragDropEffects.Move
                : e.DataTransfer.Contains(AudioFiles.DragFormat) || e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy
                : DragDropEffects.None;
            if (e.DragEffects != DragDropEffects.None) ShowDropIndicator(e);
            StopFollowing(surely: true); // the list stays still under what is being dropped
        });
        Playlist.AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropIndicator.IsVisible = false);
        Playlist.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            DropIndicator.IsVisible = false;
            int insertAt = DropIndex(e);

            if (e.DataTransfer.TryGetValue(RowDragFormat) is { } ids)
            {
                _engine?.Move(ids, EngineIndex(insertAt));
                return;
            }
            var paths = e.DataTransfer.TryGetValue(AudioFiles.DragFormat)
                ?? e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths is { Length: > 0 }) AddPaths(paths, _rows.Count == 0 ? -1 : EngineIndex(insertAt));
        });

        Library.AddRequested += (paths, asNext) => AddPaths(paths, asNext ? NextInsertIndex() : -1, reveal: true);
        Tracks.AddRequested += (paths, asNext) => AddPaths(paths, asNext ? NextInsertIndex() : -1, reveal: true);
        Tracks.EditCuesRequested += async path => await EditCues(path, Path.GetFileNameWithoutExtension(path));
        Tracks.EditTagsRequested += async path => await EditTags(path);
        // Clicking the open tab keeps it open (ShowSideTab sets all the buttons again).
        CartwallTab.Click += (_, _) => ShowSideTab("cartwall");
        LibraryTab.Click += (_, _) => ShowSideTab("library");
        FoldersTab.Click += (_, _) => ShowSideTab("folders");
        ShowSideTab(_settings.SideTab);
        double share = Math.Clamp(_settings.SideShare, 0.2, 0.7);
        Body.ColumnDefinitions[0].Width = new GridLength(1 - share, GridUnitType.Star);
        Body.ColumnDefinitions[2].Width = new GridLength(share, GridUnitType.Star);
        ScanButton.Click += async (_, _) =>
        {
            if (_library.IsScanning) _library.CancelScan();
            else await _library.ScanAsync(_settings.Library.Folders);
        };
        Library.RootChanged += root => _settings.MusicFolder = root;
        Library.Categories = () =>
        {
            try { return _library.Db?.Categories() is { } all ? Playout.Library.CategoryTree.Flatten(all).Select(x => (x.Category.Id, Playout.Library.CategoryTree.PathOf(all, x.Category.Id))).ToList() : []; }
            catch { return []; }
        };
        Library.CategoryOf = folder =>
        {
            try { return _library.Db?.FolderCategories().TryGetValue(folder, out long id) == true ? id : null; }
            catch { return null; }
        };
        Library.FolderCategoryRequested += async (folder, id) => await SetFolderCategory(folder, id);
        Library.EditCuesRequested += async path => await EditCues(path, Path.GetFileNameWithoutExtension(path));
        Library.EditTagsRequested += async path => await EditTags(path);

        StreamToggle.Click += (_, _) => ToggleStreaming();
        // Right-click: explicit start and stop; Stop also clears an error (a click on an error retries).
        var startStream = new MenuItem { Header = L.T("Start streaming") };
        startStream.Click += (_, _) => { if (_engine?.GetStatus().Stream is StreamState.Off or StreamState.Failed) ToggleStreaming(); };
        var stopStream = new MenuItem { Header = L.T("Stop streaming") };
        stopStream.Click += (_, _) => _engine?.StopStreaming();
        var streamMenu = new ContextMenu { Items = { startStream, stopStream } };
        streamMenu.Opening += (_, _) =>
        {
            var state = _engine?.GetStatus().Stream ?? StreamState.Off;
            startStream.IsEnabled = state is StreamState.Off or StreamState.Failed;
            stopStream.IsEnabled = state != StreamState.Off;
            stopStream.Header = state switch
            {
                StreamState.Connecting => L.T("Cancel connecting"),
                StreamState.Failed => L.T("Turn streaming off"),
                _ => L.T("Stop streaming"),
            };
        };
        StreamToggle.ContextMenu = streamMenu;
        SettingsButton.Click += async (_, _) => await OpenSettings();
        ReportsButton.Click += (_, _) => OpenReports();

        // Tunnel: the operator's shortcuts come before whatever control has focus (Space on a button, keys in the playlist).
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShortcutKeyUp, RoutingStrategies.Tunnel);
        KeyDown += OnWindowKeyDown;
        // Releasing the mic key in another window never reaches us: close a held mic when focus leaves (a mic
        // switched on with a press stays on).
        Deactivated += (_, _) =>
        {
            _keysDown.Clear();
            _micKeyHeld = false;
            UpdateMic();
        };
        Closing += (_, _) =>
        {
            double playlist = Body.ColumnDefinitions[0].ActualWidth, side = Body.ColumnDefinitions[2].ActualWidth;
            if (playlist + side > 0) _settings.SideShare = Math.Round(side / (playlist + side), 3);
            SaveSettings();
        };
        Closed += (_, _) => _rds?.Dispose();
        Closed += (_, _) => StopGpio();
        Closed += (_, _) => _web?.Dispose();
        Closed += (_, _) =>
        {
            // Nothing keeps running for a closed window.
            _fastTimer.Stop();
            _responseProbe?.Dispose();
            _saveTimer.Stop();
            _updateTimer?.Stop();
            _health?.Dispose();
            _takes.Dispose();
            _backups?.Dispose();
            _scheduler?.Dispose();
            LogCartsOnAir();
            _asRun.Close();
        };
        Closed += (_, _) =>
        {
            _engine?.Dispose();
            // After the settings are saved and the sound card is free, so the new version starts cleanly.
            if (_restartAfterClose)
            {
                SingleInstance.Release(); // the new version takes it
                Updates.Restart();
            }
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
        if (_locked) return; // nobody logged in
        if (Shortcuts.FromEvent(e) is not { } pressed) return;
        // Plain keys type text in a text box (e.g. searching the music folder).
        if (FocusManager?.GetFocusedElement() is TextBox && Shortcuts.TypesText(pressed)) return;
        if (_shortcuts.FirstOrDefault(s => pressed.Equals(s.Gesture)).Id is not { } id) return;
        e.Handled = true;
        if (!_keysDown.Add(e.Key)) return; // auto-repeat
        switch (id)
        {
            case Shortcuts.Play: PlayPressed(); break;
            case Shortcuts.Pause: _ = PausePressed(); break;
            case Shortcuts.Next: _ = NextPressed(); break;
            case Shortcuts.Stop: _ = StopPressed(); break;
            case Shortcuts.Mic:
                if (_settings.MicToggle) ToggleMic();
                else { _micKeyHeld = true; UpdateMic(); }
                break;
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
        ToolTip.SetTip(PlayButton, L.T("Play") + Key(Shortcuts.Play));
        ToolTip.SetTip(PauseButton, L.T("Pause / resume") + Key(Shortcuts.Pause));
        ToolTip.SetTip(NextButton, L.T("Next, crossfade now") + Key(Shortcuts.Next));
        ToolTip.SetTip(StopButton, L.T("Stop") + Key(Shortcuts.Stop));
        ToolTip.SetTip(MicButton, _settings.MicToggle
            ? L.F("Press to open the mic, press again to close it{0}. Music and cartwall fade down while the mic is open.", Key(Shortcuts.Mic))
            : L.F("Hold to talk{0}. Music and cartwall fade down while the mic is open.", Key(Shortcuts.Mic)));
        // The mode may have changed: start closed.
        _micLatched = false;
        UpdateMic();
        ShowCartShortcuts();
        _lastStreamState = null; // refresh the STREAM tooltip
    }

    string StreamKey() =>
        _shortcuts.FirstOrDefault(s => s.Id == Shortcuts.Stream).Gesture is { } g ? $" ({Shortcuts.Display(g)})" : "";

    /// <summary>The shortcut for a cart, as shown on it ("" if none).</summary>
    string CartShortcut(int index) =>
        _shortcuts.FirstOrDefault(s => s.Id == Shortcuts.Cart(index)).Gesture is { } g ? Shortcuts.Display(g) : "";

    // ───────────────────────── Adverts on air ─────────────────────────
    // An advert is paid air time: Next, Pause, Stop and putting another item on air ask first while one plays
    // (with no Enter shortcut in the question), so it isn't cut by accident but still can be in an emergency.

    static bool AdvertOnAir(PlayerStatus st) => st.State == PlayerState.Playing && st.Current?.Kind == TrackKind.Commercial;

    bool _askingAboutAdvert;

    /// <summary>True if nothing is lost by <paramref name="action"/>, or the operator says to do it anyway.</summary>
    async Task<bool> MayCutAdvert(string action, string question)
    {
        if (_engine?.GetStatus() is not { } st || !AdvertOnAir(st)) return true;
        if (_askingAboutAdvert) return false; // the question is already open
        _askingAboutAdvert = true;
        try
        {
            string what = st.Current!.Advertiser is { Length: > 0 } client ? $"{st.Current.Display} ({client})" : st.Current.Display;
            return await Dialogs.Confirm(this, L.T("Advert on air"), L.F("An advert is on air: {0}.", what) + "\n\n" + question, action,
                enterConfirms: false);
        }
        finally
        {
            _askingAboutAdvert = false;
        }
    }

    async Task NextPressed()
    {
        if (await MayCutAdvert(L.T("Next"), L.T("Cut it and go to the next item now?"))) _engine?.Next();
    }

    async Task PausePressed()
    {
        // Resuming is always fine.
        if (_engine?.GetStatus().State == PlayerState.Paused || await MayCutAdvert(L.T("Pause"), L.T("Pause it anyway?"))) _engine?.TogglePause();
    }

    async Task StopPressed()
    {
        if (await MayCutAdvert(L.T("Stop"), L.T("Stop it anyway?"))) _engine?.Stop();
    }

    async Task PlayNow(PlaylistRow row)
    {
        if (await MayCutAdvert(L.T("Play now"), L.F("Cut it and put \"{0}\" on air now?", row.Track.Display))) _engine?.Play(row.Id);
    }

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_locked) return;
        var cmd = CommandModifiers;
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers == cmd: _ = AddFilesFromPicker(); break;
            case Key.F when e.KeyModifiers == cmd: ShowSideTab("library"); Tracks.FocusSearch(); break;
            case Key.S when e.KeyModifiers == cmd: _ = SavePlaylist(); break;
            case Key.E when e.KeyModifiers == cmd: _ = EditSelectedCues(); break;
            case Key.G when e.KeyModifiers == cmd: _ = EditSelectedSegue(); break;
            case Key.T when e.KeyModifiers == cmd: _ = EditSelectedTags(); break;
            case Key.Up when e.KeyModifiers == cmd: MoveSelected(-1); break;
            case Key.Down when e.KeyModifiers == cmd: MoveSelected(+1); break;
            case Key.OemComma when e.KeyModifiers == cmd: _ = OpenSettings(); break;
            default: return;
        }
        e.Handled = true;
    }

    bool _micLatched; // switched on with a press (MicToggle)

    /// <summary>A press of the MIC button or key when it switches the mic on and off.</summary>
    void ToggleMic()
    {
        if (!_settings.MicToggle) return;
        _micLatched = !_micLatched;
        UpdateMic();
    }

    /// <summary>
    /// The mic is open while the MIC button or its key is held, or (MicToggle) from one press to the next. Web radio only.
    /// </summary>
    void UpdateMic()
    {
        bool on = MicButton.IsVisible && (_settings.MicToggle ? _micLatched : MicButton.IsPressed || _micKeyHeld);
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
        PlayerStatus st;
        try { st = _engine.GetStatus(); }
        catch (Exception ex)
        {
            TickFailed("status", ex);
            return;
        }
        // Each part on its own: one that fails (a file, a platform quirk) doesn't stop the rest of the deck.
        Part("rds", () => _rds?.Update(st.State == PlayerState.Stopped ? null : st.Current));
        Part("meters", () =>
        {
            MeterL.Push(st.LevelLeft);
            MeterR.Push(st.LevelRight);
        });
        Part("now playing", () =>
        {
            NowTitle.Text = st.Current?.Title ?? L.T("Nothing on air");
            NowArtist.Text = string.IsNullOrWhiteSpace(st.Current?.Artist) ? " " : st.Current!.Artist;
        });
        Part("waveform", () => UpdateWaveforms(st));
        Part("cover", () => ShowCover(st.Current));
        Part("times", () =>
        {
            // Times run from Cue In to Cue Out, not over the whole file.
            bool hasTrack = st.Current != null;
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
        });
        Part("buttons", () =>
        {
            PlayButton.Classes.Set("active", st.State == PlayerState.Playing);
            PauseButton.Classes.Set("active", st.State == PlayerState.Paused);
            StopButton.Classes.Set("active", st.State == PlayerState.Stopped);
            bool advert = AdvertOnAir(st);
            foreach (var b in new[] { NextButton, PauseButton, StopButton }) b.Classes.Set("guarded", advert);
            MicButton.Classes.Set("live", _engine.MicLive);
            if (st.Current?.Id != _lastOnAirId)
            {
                // A new item went on air (Next button, double-click or auto-advance): flash Next.
                if (st.Current != null && _lastOnAirId != null) FlashNext();
            // The sound changed: the list follows the item on air again.
            _follow = true;
            ScrollToPlaying();
                if (st.Current != null) CountPlayed();
                _lastOnAirId = st.Current?.Id;
            }

            (OnAirText.Text, OnAirBadge.Background) = st.State switch
            {
                PlayerState.Playing => (L.T("ON AIR"), (IBrush)this.FindResource("OnAir")!),
                PlayerState.Paused => (L.T("PAUSED"), (IBrush)this.FindResource("NextUp")!),
                _ => (L.T("STOPPED"), new SolidColorBrush(Color.Parse("#3A3F4B"))),
            };
        });
        Part("carts", UpdateCarts);
        Part("watch", () => WatchDisplay(st));
        Part("break countdown", () => UpdateBreakCountdown(now));
        Part("stream", () => UpdateStreamStatus(st));
        Part("gpio", () => ShowGpio(st));

        // Once a second is plenty for the date, ETA column and station info.
        if (_slowTick-- > 0) return;
        _slowTick = 30;
        Part("date", () => DateText.Text = now.ToString("dddd, d MMMM yyyy"));
        Part("upcoming", () => ReadUpcoming(now));
        Part("as-run", () => _asRun.Sync()); // every ten minutes: what the database missed
        Part("start times", () => UpdateStartTimes(st));
        // In AUTO, playing or not: an empty playlist gets what is planned, ready to start (it never starts by itself).
        Part("daily playlist", () => _plan?.Check(_playlistEnds, _engine.AutoAdvance, _order));
        Part("station", () => UpdateStationInfo(now));
        Part("station settings", () => SyncStationSettings(now));
        Part("website", () => _web?.Update(st.State == PlayerState.Stopped ? null : st.Current, NextItem()));
    }

    double _lastPosition = -1;
    long _positionSince = Environment.TickCount64, _soundSince, _lastDiagnosis;
    Guid? _watchedId;
    PlayerState _watchedState;

    /// <summary>
    /// The display should follow the sound: when it doesn't (sound coming out but the player not playing, or playing
    /// with the position stuck for 3 s), what the engine is doing goes in the log, at most once a minute.
    /// </summary>
    void WatchDisplay(PlayerStatus st)
    {
        long now = Environment.TickCount64;
        // A new item or a change of state starts the count again.
        if (Math.Abs(st.Position - _lastPosition) > 0.001 || st.Current?.Id != _watchedId || st.State != _watchedState)
        {
            _lastPosition = st.Position;
            _positionSince = now;
            _watchedId = st.Current?.Id;
            _watchedState = st.State;
        }
        bool sound = Math.Max(st.LevelLeft, st.LevelRight) > 0.05f;
        _soundSince = sound && st.State != PlayerState.Playing ? (_soundSince == 0 ? now : _soundSince) : 0;
        string? problem = st.State == PlayerState.Playing && st.Current is { IsOpenEnded: false } && now - _positionSince > 3000 ? "the position isn't moving"
            : _soundSince != 0 && now - _soundSince > 3000 ? "sound is coming out but the player isn't playing"
            : null;
        if (problem == null || now - _lastDiagnosis < 60_000 || _engine == null) return;
        _lastDiagnosis = now;
        AppLog.Write($"Display: {problem}: {_engine.Diagnose()}");
    }

    readonly Dictionary<string, long> _tickErrors = new();

    void Part(string name, Action action)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { action(); }
        catch (Exception ex) { TickFailed(name, ex); }
        Slow(name, start);
    }

    /// <summary>
    /// A part of the screen's work that took long (the window stops responding meanwhile): in the log, with how long,
    /// at most every 30 s for each part — to find what makes it slow on one computer.
    /// </summary>
    void Slow(string part, long start)
    {
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (ms < 250) return;
        long now = Environment.TickCount64;
        if (_slowParts.TryGetValue(part, out long last) && now - last < 30_000) return;
        _slowParts[part] = now;
        AppLog.Write($"Screen update slow ({part}): {ms:0} ms");
    }

    readonly Dictionary<string, long> _slowParts = new();
    Timer? _responseProbe;
    long _lastStallLogged;

    /// <summary>A part of the screen's update failed: in the log with what went wrong (at most every 10 s for each part).</summary>
    void TickFailed(string part, Exception ex)
    {
        long now = Environment.TickCount64;
        if (_tickErrors.TryGetValue(part, out long last) && now - last < 10_000) return;
        _tickErrors[part] = now;
        AppLog.Write($"Screen update failed ({part}): {ex}");
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
                            if (t.Result is { } wave) _remoteWave = (id, wave);
                            WaveMain.SetTrack(t.Result, length);
                            WaveOverview.SetTrack(t.Result, length);
                        });
                });
            }
        }

        bool playing = st.State == PlayerState.Playing;
        var markers = st.Current is { } t ? CueMarkers.For(t, _engine?.CrossfadeBetween(t, NextItem()) ?? 0) : (CueMarkers?)null;
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
        CueLabel.Text = L.T(label);
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

    /// <summary>
    /// Start times, hour dividers and the playlist end; and the rows in the order they will play, with the events and
    /// breaks the schedule will put in among them (previews: they become real items when they are due).
    /// </summary>
    void UpdateStartTimes(PlayerStatus st)
    {
        _playlistEnds = null;
        if (_snapshot == null) return;
        var items = _snapshot.Items;
        int current = _snapshot.CurrentId is { } cid ? items.ToList().FindIndex(t => t.Id == cid) : -1;
        int startFrom = _snapshot.NextId is { } nid ? items.ToList().FindIndex(t => t.Id == nid) : -1;

        // Mirrors the engine: each item starts at the previous one's mix point, and that one carries on to its Cue Out.
        // The crossfade is song to song; with a jingle or advert on either side, the other one.
        double Fade(Track t, Track? next) => _engine?.CrossfadeBetween(t, next) ?? 0;
        // Where the next starts: the mix point, or earlier for a voice track over the ending.
        double MixOf(Track t, Track? next) => next?.Overlap is > 0
            ? Math.Max(t.Start, Math.Min(t.MixPoint(Fade(t, next)), t.End - next.Overlap))
            : t.MixPoint(Fade(t, next));
        double Tail(Track t, Track? next) => t.End - MixOf(t, next);
        var nextUp = startFrom >= 0 ? items[startFrom] : null;
        double lastTail = st.Current is { } cur ? Tail(cur, nextUp) : 0;

        // A stream that plays until Stop has no end, so nothing after it has a start time.
        DateTime? at = st.State == PlayerState.Playing && current >= 0 && st.Current is { IsOpenEnded: false }
            ? DateTime.Now.AddSeconds(Math.Max(0, MixOf(st.Current, nextUp) - st.Position))
            : null;

        var order = new List<PlaylistRow>(items.Count + _upcoming.Count);
        var pending = new Queue<UpcomingEvent>(_upcoming.OrderBy(u => u.Start));
        _nextBreak = null;
        _nextFixed = null;
        // Played items (and the one on air) show when they went on air, with a divider where a new hour started; then
        // the first item starting in a later hour than that gets one, and so on.
        DateTime? pastHour = null;
        bool PlayedRow(int i) => items[i].PlayedAt != null && (i <= current || _snapshot.Played.Contains(items[i].Id));
        for (int i = 0; i < items.Count; i++)
        {
            if (!PlayedRow(i) || !_rowsById.TryGetValue(items[i].Id, out var row)) continue;
            row.HourMark = "";
            row.IsOverHour = false;
            var played = items[i].PlayedAt!.Value;
            var local = played.ToLocalTime();
            row.StartTime = local.Date == DateTime.Today ? local.ToString("HH:mm:ss") : local.ToString("ddd HH:mm");
            row.StartAt = local;
            var h = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
            if (pastHour is { } ph && h > ph) row.HourMark = h.Date == DateTime.Today ? h.ToString("HH:mm") : h.ToString("HH:mm  ddd");
            if (pastHour == null || h > pastHour) pastHour = h;
        }
        var lastHour = pastHour ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
        void Show(PlaylistRow row, DateTime t)
        {
            row.StartTime = t.ToString("HH:mm:ss");
            row.StartAt = t;
            row.IsOverHour = PlanPlayer.OverHour(row, t);
            // The first ad break still to come (shown ahead, or already in the playlist), for the countdown.
            // The first thing with a fixed time still to come (an event shown ahead, an ad break, a timed item), for NEXT EVENT.
            if (_nextFixed == null && !row.IsPlaying && !row.IsPlayed && t > DateTime.Now
                && (row.IsPreview || row.IsTimed || (row.IsAdBreak && !row.IsEmptyBreak)))
                _nextFixed = (t, row.Track.Event ?? (row.IsAdBreak ? L.T("Ad break") : row.Title));
            if (_nextBreak == null && row.IsAdBreak && !row.IsEmptyBreak && !row.IsPlaying && !row.IsPlayed && t > DateTime.Now)
                _nextBreak = (t, row.Track.Event ?? (row.IsPreview ? row.Title : L.T("Ad break")),
                    row.IsPreview ? _upcoming.FirstOrDefault(u => u.Label == (row.Track.Event ?? row.Title))?.How ?? "" : BreakHow(row.Track));
            var hour = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
            if (hour <= lastHour) return;
            row.HourMark = hour.Date == DateTime.Today ? hour.ToString("HH:mm") : hour.ToString("HH:mm  ddd");
            lastHour = hour;
        }
        // Scheduled events due by the time the playlist reaches t go in there: a soft one when the item before it
        // ends (t), a hard one at its time (cutting it), one that ends at its time at its start.
        DateTime Scheduled(DateTime t)
        {
            while (pending.Count > 0 && pending.Peek().Start <= t)
            {
                var u = pending.Dequeue();
                var start = u.Timing == EventStart.Soft ? t : u.Start;
                var itemAt = start;
                foreach (var row in PreviewRows(u))
                {
                    Show(row, itemAt);
                    order.Add(row);
                    itemAt = itemAt.AddSeconds(row.Track.Duration);
                }
                t = start.AddSeconds(u.Seconds);
                lastTail = 0;
            }
            return t;
        }

        // The next item with a latest start after i, and when (a break, an event).
        (string Name, DateTime At)? NextTimed(int i)
        {
            for (int k = i + 1; k < items.Count; k++)
                if (items[k].StartBy is { } by && !PlayedRow(k)) return (items[k].Event ?? items[k].Display, by.ToLocalTime());
            return null;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (!_rowsById.TryGetValue(items[i].Id, out var row)) continue;
            if (PlayedRow(i))
            {
                order.Add(row); // shown above, with when it played
                continue;
            }
            row.HourMark = "";
            if (at != null && startFrom >= 0 && i >= startFrom)
            {
                at = Scheduled(at.Value);
                // An item with a latest start starts then at the latest (what is on air is faded).
                if (items[i].StartBy is { } by && by.ToLocalTime() < at) at = by.ToLocalTime();
                var t = at.Value;
                Show(row, t);
                // One that would only start once a timed item ahead has to have started won't play here (jumped over).
                row.CutBy = null;
                if (items[i].StartBy == null && items[i].Event == null && NextTimed(i) is { } ahead && t >= ahead.At.AddSeconds(-0.5))
                {
                    row.CutBy = ahead;
                    row.IsOverHour = true;
                    row.StartTime = "—";
                }
                lastTail = Tail(items[i], i + 1 < items.Count ? items[i + 1] : null);
                at = items[i].IsOpenEnded ? null : t.AddSeconds(items[i].PlayLength - lastTail);
            }
            else
            {
                row.StartTime = "";
                row.IsOverHour = false;
                row.StartAt = i == current && items[i].PlayedAt is { } on ? on.ToLocalTime() : null;
            }
            order.Add(row);
        }

        // When the last item finishes: the chain above stops at its mix point; with nothing queued it is the current item.
        if (at is { } end) _playlistEnds = end.AddSeconds(lastTail);
        // What comes after the playlist's end (or with the player stopped): at its own time.
        while (pending.Count > 0)
        {
            var u = pending.Dequeue();
            var itemAt = u.Start;
            foreach (var row in PreviewRows(u))
            {
                Show(row, itemAt);
                order.Add(row);
                itemAt = itemAt.AddSeconds(row.Track.Duration);
            }
        }
        ShowRows(order);
    }

    (DateTime At, string Name, string How)? _nextBreak;
    (DateTime At, string Name)? _nextFixed;

    /// <summary>How a break already in the playlist starts (from its first item).</summary>
    static string BreakHow(Track first) => first.StartBy is { } by
        ? first.Backtime ? L.T("ends on time") : L.F("can wait until {0:HH:mm:ss}", by.ToLocalTime())
        : L.T("after the song");

    /// <summary>
    /// "€ BREAK IN 04:32 · hard start at 12:20:00" on the deck when the next ad break is less than 10 minutes away;
    /// red in its last minute.
    /// </summary>
    void UpdateBreakCountdown(DateTime now)
    {
        var left = _nextBreak is { } b ? b.At - now : TimeSpan.MaxValue;
        bool show = left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(10);
        BreakBadge.IsVisible = show;
        if (!show) return;
        BreakSign.Text = PlaylistRow.Currency;
        BreakTime.Text = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
        BreakHowText.Text = _nextBreak!.Value.How.Length > 0 ? "· " + _nextBreak.Value.How.ToUpperInvariant() : "";
        BreakBadge.Classes.Set("soon", left <= TimeSpan.FromMinutes(1));
        ToolTip.SetTip(BreakBadge, L.F("{0}: starts about {1:HH:mm:ss}", _nextBreak.Value.Name, _nextBreak.Value.At));
    }

    // ───────────────────────── Following the item on air ─────────────────────────

    /// <summary>The list keeps the item on air at the top; scrolling by hand stops that until the item on air changes.</summary>
    bool _follow = true;
    bool _autoScrolling;
    ScrollViewer? _playlistScroll;

    void WirePlaylistView()
    {
        HourViewButton.IsChecked = _settings.PlaylistHourView;
        HourViewButton.IsCheckedChanged += (_, _) =>
        {
            _settings.PlaylistHourView = HourViewButton.IsChecked == true; // saved with the rest every few seconds
            // Either way, straight to the item on air, and follow it again.
            _follow = true;
            ShowRows(_order);
            ScrollToPlaying();
        };
        OnAirButton.Click += (_, _) =>
        {
            _follow = true;
            ScrollToPlaying();
        };
        // A wheel, the scroll bar or the keys move the list: the operator is looking at something, stop following.
        Playlist.AddHandler(PointerWheelChangedEvent, (_, _) => StopFollowing(surely: true), RoutingStrategies.Tunnel);
        Playlist.TemplateApplied += (_, _) => HookScroll();
        HookScroll();
    }

    void HookScroll()
    {
        if (_playlistScroll != null || Playlist.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll) return;
        _playlistScroll = scroll;
        scroll.ScrollChanged += (_, e) =>
        {
            // Only a move the operator made: not the rows being updated, nor our own scrolling (the list only lays out
            // the rows in view, so its height changes while scrolling: that alone says nothing).
            if (!_autoScrolling && e.OffsetDelta.Y != 0 && e.ViewportDelta.Y == 0) StopFollowing();
        };
    }

    /// <param name="surely">The wheel or a drag: the operator's, even while the list is being put in place.</param>
    void StopFollowing(bool surely = false)
    {
        if (_autoScrolling && !surely) return;
        _follow = false;
        OnAirButton.Classes.Set("attention", true);
    }

    /// <summary>Puts the item on air at the top of the list (after the layout, so its row is in place).</summary>
    void ScrollToPlaying()
    {
        OnAirButton.Classes.Set("attention", false);
        HookScroll();
        if (_playlistScroll is not { } scroll || _rows.FirstOrDefault(r => r.IsPlaying) is not { } row) return;
        int index = _rows.IndexOf(row);
        // Already at the top: nothing to do (most of the time, as this runs every second while following).
        if (Playlist.ContainerFromIndex(index) is { } shown && shown.TranslatePoint(new Point(0, 0), scroll) is { } at && Math.Abs(at.Y) < 1) return;
        _autoScrolling = true;
        // Straight there in one step: every row is as tall as the others (plus the hour divider on some), so where it
        // is can be worked out without scrolling the list through everything before it.
        MeasureRows();
        double y = 0;
        for (int i = 0; i < index; i++) y += _rowHeight + (_rows[i].HasHourMark ? _hourMarkHeight : 0);
        scroll.Offset = scroll.Offset.WithY(Math.Max(0, y));
        // Then exactly to the top, once the rows there are laid out (a pixel or two at most).
        Dispatcher.UIThread.Post(() =>
        {
            if (Playlist.ContainerFromIndex(index) is { } item && item.TranslatePoint(new Point(0, 0), scroll) is { } p)
                scroll.Offset = scroll.Offset.WithY(Math.Max(0, scroll.Offset.Y + p.Y));
            // Its own ScrollChanged comes during the layout after this: only then is a move the operator's again.
            Dispatcher.UIThread.Post(() => _autoScrolling = false, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    double _rowHeight = 50, _hourMarkHeight = 27; // measured from the rows on screen (MeasureRows)

    void MeasureRows()
    {
        double? plain = null, marked = null;
        foreach (var c in Playlist.GetRealizedContainers())
        {
            if (c.DataContext is not PlaylistRow r || c.Bounds.Height <= 0) continue;
            if (r.HasHourMark) marked ??= c.Bounds.Height;
            else plain ??= c.Bounds.Height;
        }
        if (plain is { } p) _rowHeight = p;
        if (marked is { } m && m > _rowHeight) _hourMarkHeight = m - _rowHeight;
    }

    /// <summary>Every row, in order (the playlist and the schedule's previews); <see cref="_rows"/> is what is shown of it.</summary>
    List<PlaylistRow> _order = new();

    /// <summary>
    /// The rows to show. Never what played before the previous hour (days of it pile up; Reports has it). In the hour
    /// view, from the previous hour to the first minutes of the next (Settings → Playlist), and always the item on
    /// air; otherwise everything still to play. Items not yet timed (nothing playing) are shown.
    /// </summary>
    List<PlaylistRow> Visible(List<PlaylistRow> order)
    {
        var now = DateTime.Now;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var from = hour.AddHours(-1);
        DateTime? to = _settings.PlaylistHourView && order.Any(r => r.IsPlaying)
            ? hour.AddHours(1).AddMinutes(Math.Clamp(_settings.PlaylistNextMinutes, 0, 60))
            : null;
        return order.Where(r =>
        {
            if (r.IsPlaying) return true;
            if (r.IsPlayed) return r.StartAt is { } played && played >= from; // old history without a time: hidden
            return r.StartAt is not { } t || t >= from && (to == null || t < to);
        }).ToList();
    }

    /// <summary>Shows the rows again after the order or the view changed, and keeps the item on air at the top while following.</summary>
    void ShowRows(List<PlaylistRow> order)
    {
        _order = order;
        var visible = Visible(order);
        if (!visible.SequenceEqual(_rows))
        {
            // Rows coming and going move the list: not the operator.
            _autoScrolling = true;
            SyncRows(visible);
            Dispatcher.UIThread.Post(() => _autoScrolling = false, DispatcherPriority.Background);
        }
        if (_follow) ScrollToPlaying();
    }

    /// <summary>Puts the playlist's rows in this order, moving as few as it can (keeps the scroll position and selection).</summary>
    void SyncRows(List<PlaylistRow> order)
    {
        if (order.Count == _rows.Count && order.SequenceEqual(_rows)) return;
        var keep = order.ToHashSet();
        // Drop removed rows first: otherwise every row after them gets moved up one by one, dragging the (selected)
        // removed row to the bottom, and the list scrolls after it.
        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(_rows[i])) _rows.RemoveAt(i);
        for (int i = 0; i < order.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i], order[i])) continue;
            int existing = _rows.IndexOf(order[i]);
            if (existing >= 0) _rows.Move(existing, i);
            else _rows.Insert(i, order[i]);
        }
        while (_rows.Count > order.Count) _rows.RemoveAt(_rows.Count - 1);
    }

    // ───────────────────────── Scheduled events ahead ─────────────────────────

    List<UpcomingEvent> _upcoming = new();
    DateTime _upcomingRead = DateTime.MinValue;
    bool _upcomingReading;
    readonly Dictionary<(Guid, DateTime, int), PlaylistRow> _previewRows = new();

    /// <summary>
    /// Every 15 seconds: what the schedule will put in the playlist up to an hour after its end (at most six hours
    /// ahead), read in the background (folders and files are looked at).
    /// </summary>
    void ReadUpcoming(DateTime now, bool force = false)
    {
        if (_scheduler is not { } scheduler || _upcomingReading || (!force && now - _upcomingRead < TimeSpan.FromSeconds(15))) return;
        _upcomingReading = true;
        _upcomingRead = now;
        var until = (_playlistEnds is { } end && end > now ? end : now).AddHours(1);
        if (until > now.AddHours(6)) until = now.AddHours(6);
        Task.Run(() => scheduler.Upcoming(now, until)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _upcomingReading = false;
            if (!t.IsCompletedSuccessfully) return;
            _upcoming = t.Result;
            var live = _upcoming.Select(u => (u.EntryId, u.Due)).ToHashSet();
            foreach (var key in _previewRows.Keys.Where(k => !live.Contains((k.Item1, k.Item2))).ToList()) _previewRows.Remove(key);
            if (_engine != null) UpdateStartTimes(_engine.GetStatus());
        }));
    }

    /// <summary>
    /// The rows that show a scheduled event or break ahead, one for each item it will play (the same rows while it is
    /// upcoming); a break with no adverts, one greyed row saying so.
    /// </summary>
    List<PlaylistRow> PreviewRows(UpcomingEvent u)
    {
        var rows = new List<PlaylistRow>();
        if (u.Empty || u.Items is not { Count: > 0 } items)
        {
            rows.Add(Preview(0, new Track(Guid.Empty, "", $"{u.What} · {u.How}", u.Label, u.Seconds, u.IsBreak ? TrackKind.Commercial : TrackKind.Programme)));
            rows[0].IsEmptyBreak = u.Empty;
            return rows;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // The second line: the advertiser of an advert; on the first item, how it starts.
            string line = string.Join(" · ", new[] { item.Advertiser, i == 0 ? u.How : null }.Where(x => !string.IsNullOrEmpty(x)));
            rows.Add(Preview(i, new Track(Guid.Empty, item.Path, line, item.Title, item.Seconds, item.Kind)
                { Event = u.Label, Advertiser = item.Advertiser, InBreak = item.InBreak }));
        }
        return rows;

        PlaylistRow Preview(int index, Track track)
        {
            if (!_previewRows.TryGetValue((u.EntryId, u.Due, index), out var row))
                _previewRows[(u.EntryId, u.Due, index)] = row = new PlaylistRow(track with { Id = Guid.NewGuid() }) { IsPreview = true };
            else row.Track = track with { Id = row.Id };
            row.IsEmptyBreak = false;
            row.HourMark = "";
            return row;
        }
    }

    /// <summary>The engine's playlist position for a row position (the schedule's previews aren't in the engine).</summary>
    int EngineIndex(int rowIndex)
    {
        // Through the whole order: rows hidden by the hour view (before or after) count too.
        int at = rowIndex < _rows.Count ? _order.IndexOf(_rows[rowIndex])
            : _rows.Count > 0 ? _order.IndexOf(_rows[^1]) + 1
            : _order.Count;
        if (at < 0) at = _order.Count;
        return _order.Take(at).Count(r => !r.IsPreview);
    }

    void UpdateStreamStatus(PlayerStatus st)
    {
        if (st.Stream == _lastStreamState) return;
        if (st.Stream == StreamState.OnAir) _streamSince = DateTime.Now;
        _lastStreamState = st.Stream;
        ShowStreaming();

        (StreamText.Text, StreamDot.Fill) = st.Stream switch
        {
            StreamState.OnAir => (L.T("STREAMING"), (IBrush)Brushes.White),
            StreamState.Connecting => (L.T("CONNECTING…"), (IBrush)this.FindResource("NextUp")!),
            StreamState.Failed => (L.T("STREAM ERROR"), (IBrush)this.FindResource("OnAir")!),
            _ => (L.T("STREAM OFF"), new SolidColorBrush(Color.Parse("#8B93A3"))),
        };
        StreamToggle.Classes.Set("onair", st.Stream == StreamState.OnAir);
        StreamToggle.Classes.Set("connecting", st.Stream == StreamState.Connecting);
        StreamToggle.Classes.Set("failed", st.Stream == StreamState.Failed);
        ToolTip.SetTip(StreamToggle, st.Stream switch
        {
            StreamState.OnAir => L.F("Streaming — click to stop{0}", StreamKey()),
            StreamState.Connecting => L.F("Connecting — click to cancel{0}", StreamKey()),
            StreamState.Failed => L.F("{0}\nClick to retry, or right-click to turn streaming off", st.StreamError),
            _ => L.F("Stream off — click to start (server is set in Settings){0}", StreamKey()),
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
            : L.T("Off");

        // The nearest of the schedule's next event and the playlist's next item with a fixed time.
        (DateTime At, string Name)? next = _scheduler?.Next(now) is { } e ? (e.At, string.IsNullOrWhiteSpace(e.Name) ? L.T("Event") : e.Name) : null;
        if (_nextFixed is { } f && f.At > now && (next == null || f.At < next.Value.At)) next = f;
        NextEventPanel.IsVisible = next != null;
        if (next is { } n)
        {
            NextEventText.Text = L.F("{0:HH:mm:ss}  {1}", n.At, n.Name) + (n.At.Date != now.Date ? L.T(" (tomorrow)") : "");
            var left = n.At - now;
            NextEventCountdown.Text = left.TotalHours >= 1 ? "-" + left.ToString(@"h\:mm\:ss") : "-" + left.ToString(@"mm\:ss");
            // Amber in the last minute, red in the last ten seconds.
            NextEventCountdown.Foreground = left <= TimeSpan.FromSeconds(10) ? (IBrush)this.FindResource("OnAir")!
                : left <= TimeSpan.FromMinutes(1) ? Amber : Brushes.White;
        }
        UpdateLights();
    }

    static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F5A524")), LightOk = new SolidColorBrush(Color.Parse("#3ECF8E")),
        LightOff = new SolidColorBrush(Color.Parse("#4A5060")), LightBad = new SolidColorBrush(Color.Parse("#E5484D"));
    readonly Dictionary<string, (Ellipse Dot, Border Pill)> _lights = new();

    /// <summary>A status light in the status bar: its dot, name and tooltip.</summary>
    void Light(string name, IBrush colour, string tip)
    {
        if (!_lights.TryGetValue(name, out var light))
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                Padding = new Thickness(7, 2), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.Parse("#1B1F27")),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 5,
                    Children = { dot, new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
                },
            };
            Lights.Children.Add(pill);
            _lights[name] = light = (dot, pill);
        }
        light.Dot.Fill = colour;
        ToolTip.SetTip(light.Pill, tip);
    }

    /// <summary>Stream, RDS, Sintoniza Rec, the library and alerts at a glance: green working, red a problem, grey off.</summary>
    void UpdateLights()
    {
        var st = _lastStreamState;
        if (!_settings.UsesStreaming) Light(L.T("STREAM"), LightOff, L.T("No stream set up"));
        else Light(L.T("STREAM"), st switch { StreamState.OnAir => LightOk, StreamState.Connecting => Amber, StreamState.Failed => LightBad, _ => LightOff },
            st switch
            {
                StreamState.OnAir => L.T("Streaming"),
                StreamState.Connecting => L.T("Connecting to the stream server"),
                StreamState.Failed => L.T("The stream can't connect"),
                _ => L.T("Stream off"),
            });

        if (_rds == null) Light("RDS", LightOff, L.T("RDS off (Settings)"));
        else Light("RDS", _rds.Error == null ? LightOk : LightBad, _rds.Error == null ? L.T("RDS: sending to the encoder") : L.F("RDS: can't send ({0})", _rds.Error));

        var rec = RecService.ReadStatus();
        Light("REC", rec == null ? LightOff : !rec.Recording ? LightBad : rec.SilentSeconds >= 30 ? Amber : LightOk,
            rec == null ? L.T("Sintoniza Rec isn't running on this computer")
            : !rec.Recording ? L.T("Sintoniza Rec is open but NOT recording")
            : rec.SilentSeconds >= 30 ? L.F("Sintoniza Rec is recording silence ({0:0} s)", rec.SilentSeconds)
            : L.F("Sintoniza Rec is recording: {0}", Path.GetFileName(rec.File ?? "")));

        Light(L.T("LIBRARY"), _library.Db != null ? LightOk : _library.Error != null ? LightBad : LightOff,
            _library.Db != null ? L.T("The library is open") : _library.Error is { } error ? L.F("The library can't be reached: {0}", error) : L.T("No library"));

        var problems = _health?.Active ?? [];
        var alerts = _settings.Alerts;
        Light(L.T("ALERTS"), problems.Count > 0 ? LightBad : alerts.Enabled && alerts.HasChannel ? LightOk : LightOff,
            problems.Count > 0 ? string.Join("\n", problems) : alerts.Enabled && alerts.HasChannel ? L.T("Alerts on: nothing wrong") : L.T("Alerts off (Settings)"));
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

        // The rows in their order, with what the schedule will add among them.
        UpdateStartTimes(_engine?.GetStatus() ?? default);

        foreach (var row in _rows.Where(r => selected.Contains(r.Id)))
            if (Playlist.SelectedItems?.Contains(row) == false) Playlist.SelectedItems.Add(row);
        RevealAdded();

        EmptyHint.IsVisible = _rows.Count == 0;
        double total = snap.Items.Sum(t => t.PlayLength);
        PlaylistInfo.Text = snap.Items.Count == 0 ? "" : L.F("{0} items · {1}", snap.Items.Count, Format.Duration(total));
        NextTitle.Text = snap.NextId is { } n && _rowsById.TryGetValue(n, out var next) ? next.Track.Display : "—";
        ShowNotes(snap);

        // Not while the saved playlist is still being added: a save then would lose the rest of it.
        if (_restored) _settings.PlaylistItems = snap.Items.Select(PlaylistEntry.From).ToList();
    }

    /// <summary>The presenter's notes: the one of the item on air, and the next item's (to prepare).</summary>
    void ShowNotes(PlaylistSnapshot snap)
    {
        string? Note(Guid? id) => id is { } i ? snap.Items.FirstOrDefault(t => t.Id == i)?.Note : null;
        string? now = Note(snap.CurrentId), next = Note(snap.NextId);
        NoteNowText.Text = string.IsNullOrWhiteSpace(now) ? "" : "📝 " + now;
        NoteNowText.IsVisible = NoteNowText.Text.Length > 0;
        NoteNextText.Text = string.IsNullOrWhiteSpace(next) ? "" : L.F("📝 NEXT · {0}", next);
        NoteNextText.IsVisible = NoteNextText.Text.Length > 0;
        NoteBox.IsVisible = NoteNowText.IsVisible || NoteNextText.IsVisible;
    }

    void RemoveSelected()
    {
        foreach (var row in Playlist.SelectedItems?.OfType<PlaylistRow>().ToList() ?? new())
            _engine?.Remove(row.Id);
    }

    void MoveSelected(int delta)
    {
        // In the engine's playlist (the schedule's previews aren't in it), all of it (the hour view may hide some).
        var real = _order.Where(r => !r.IsPreview).ToList();
        var selected = Playlist.SelectedItems?.OfType<PlaylistRow>().Select(r => real.IndexOf(r)).Where(i => i >= 0).Order().ToList();
        if (selected is not { Count: > 0 }) return;
        int insertAt = delta < 0 ? selected[0] - 1 : selected[^1] + 2;
        if (insertAt < 0 || insertAt > real.Count) return;
        _engine?.Move(selected.Select(i => real[i].Id).ToList(), insertAt);
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
            Title = L.T("Add audio files"),
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }, FilePickerFileTypes.All],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) _engine?.AddFiles(paths);
    }

    async Task OpenPlaylist()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Open playlist"),
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

    /// <summary>
    /// Played items saved without when they went on air (by a version before that was kept) get it from the as-run
    /// log: going back from the item that was on air, each takes the latest play of its file before the item after it
    /// (one played out of order: its latest play not taken by another row).
    /// </summary>
    static void FillPlayedTimes(List<PlaylistEntry> items, int onAirIndex)
    {
        int last = Math.Min(onAirIndex, items.Count - 1);
        if (last < 0 || items.Take(last + 1).All(e => e.PlayedAt != null)) return;
        List<AsRunEntry> log;
        try { log = AsRunLog.Read(AsRunLog.DefaultFolder, DateTime.Today.AddDays(-7), DateTime.Today).Where(e => e.Source != "cart").ToList(); }
        catch { return; }
        var before = DateTime.MaxValue;
        var used = new HashSet<AsRunEntry>();
        for (int i = last; i >= 0; i--)
        {
            var e = items[i];
            if (e.PlayedAt is { } known)
            {
                before = known.ToLocalTime();
                continue;
            }
            if (log.LastOrDefault(x => x.Start < before && x.File == e.Path && !used.Contains(x)) is { } play)
            {
                before = play.Start;
            }
            // Played out of order (the host went ahead and came back): its latest play not taken by another row.
            else if (log.LastOrDefault(x => x.File == e.Path && !used.Contains(x)) is not { } other) continue;
            else play = other;
            used.Add(play);
            e.PlayedAt = play.Start.ToUniversalTime();
        }
    }

    /// <summary>A saved item that can still play: a file that is there, a stream, a take (put together when it plays).</summary>
    static bool Playable(PlaylistEntry e) => e.IsStream || Takes.IsTake(e.Path) || File.Exists(e.Path);

    /// <summary>Adds saved or loaded playlist items in order: files that still exist, and streams.</summary>
    void AddEntries(IEnumerable<PlaylistEntry> entries)
    {
        if (_engine is not { } engine) return;
        var files = new List<string>();
        foreach (var e in entries)
        {
            if (e.Event != null)
            {
                if (!Playable(e)) continue;
                Flush();
                engine.AddScheduled(e.Event, [e.ToScheduled()]);
                continue;
            }
            if (e.HasDetails)
            {
                if (!Playable(e)) continue;
                Flush();
                engine.AddItems([e.ToScheduled()]);
                continue;
            }
            if (!e.IsStream && !Takes.IsTake(e.Path))
            {
                if (File.Exists(e.Path)) files.Add(e.Path);
                continue;
            }
            Flush();
            if (Takes.IsTake(e.Path)) engine.AddItems([e.ToScheduled()]);
            else engine.AddStream(e.Path, e.StreamName!, e.StreamSeconds);
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
            Title = L.T("Save playlist"),
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
    /// <param name="reveal">
    /// From the library (double-click, Enter, menu): select the new items and scroll to them, as at the end of a long
    /// playlist they would otherwise go unseen; and say so when a file isn't there.
    /// </param>
    void AddPaths(IReadOnlyList<string> paths, int insertAt, bool reveal = false)
    {
        if (_engine is not { } engine) return;
        Task.Run(() =>
        {
            var files = AudioFiles.Expand(paths).ToList();
            if (files.Count == 0)
            {
                if (reveal) Dispatcher.UIThread.Post(() => LastLog.Text = L.F("Not added, the file isn't there: {0}", paths.FirstOrDefault()));
                return;
            }
            // Posted before the engine's own change, which comes after this on the UI thread.
            if (reveal) Dispatcher.UIThread.Post(() => _reveal = (files.ToHashSet(), _rowsById.Keys.ToHashSet(), DateTime.UtcNow));
            engine.AddFiles(files, insertAt);
        });
    }

    /// <summary>Items being added from the library, to show once they are in the playlist (<see cref="RevealAdded"/>).</summary>
    (HashSet<string> Paths, HashSet<Guid> Before, DateTime At)? _reveal;

    void RevealAdded()
    {
        if (_reveal is not { } r) return;
        if (DateTime.UtcNow - r.At > TimeSpan.FromSeconds(10)) { _reveal = null; return; } // the files couldn't be read
        var added = _order.Where(x => !r.Before.Contains(x.Id) && r.Paths.Contains(x.Track.Path)).ToList();
        if (added.Count == 0) return;
        _reveal = null;
        LastLog.Text = added.Count == 1
            ? L.F("Added to the playlist: {0}", added[0].Track.Display)
            : L.F("{0} added to the playlist", added.Count);
        var shown = added.Where(_rows.Contains).ToList(); // the hour view may not show them yet
        if (shown.Count == 0) return;
        StopFollowing(surely: true);
        Playlist.SelectedItems?.Clear();
        foreach (var row in shown) Playlist.SelectedItems?.Add(row);
        Playlist.ScrollIntoView(shown[^1]);
    }

    /// <summary>Position right after the on-air item, so "Play next" items go ahead of the rest.</summary>
    int NextInsertIndex()
    {
        if (_snapshot?.CurrentId is not { } id) return _snapshot?.NextId is { } n ? EngineIndex(_rows.IndexOf(_rowsById[n])) : -1;
        return _rowsById.TryGetValue(id, out var row) ? EngineIndex(_rows.IndexOf(row)) + 1 : -1;
    }

    // ───────────────────────── Cue points ─────────────────────────

    async Task EditSelectedCues()
    {
        if (Playlist.SelectedItem is PlaylistRow { Track.IsStream: false, IsPreview: false } row) await EditCues(row.Track.Path, row.Track.Display);
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

    /// <summary>The segue editor for the selected item and the one after it in the playlist.</summary>
    async Task EditSelectedSegue()
    {
        if (_engine is not { } engine || _editorOpen) return;
        if (Playlist.SelectedItem is not PlaylistRow { IsPreview: false } row) return;
        var after = _rows.Skip(_rows.IndexOf(row) + 1).FirstOrDefault(r => !r.IsPreview);
        if (row.Track.IsStream || after == null || after.Track.IsStream)
        {
            LastLog.Text = L.T("A segue needs two files one after the other: select the first of them.");
            return;
        }
        _editorOpen = true;
        try
        {
            var host = new SegueHost
            {
                Open = engine.OpenPreview,
                GetCues = engine.GetCues,
                SaveCues = (path, cues) => { engine.SetCues(path, cues); return Task.CompletedTask; },
                LoadWaveform = engine.LoadWaveformAsync,
                Crossfade = (a, b) => engine.CrossfadeBetween(new Track(Guid.Empty, "", "", "", 0, a), new Track(Guid.Empty, "", "", "", 0, b)),
                PreviewIsOnAir = engine.PreviewIsOnAir,
            };
            await new SegueEditorWindow(host, new SegueItem(row.Track.Path, row.Track.Display, row.Track.Kind),
                new SegueItem(after.Track.Path, after.Track.Display, after.Track.Kind)).ShowDialog<bool>(this);
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
        if (Playlist.SelectedItem is PlaylistRow { Track.IsStream: false, IsPreview: false } row) await EditTags(row.Track.Path);
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
            LastLog.Text = L.T("Set up the streaming server in Settings first");
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
                UpdateButton.Content = L.F("Update {0} available", _update.Version);
                ToolTip.SetTip(UpdateButton, L.F("{0} {1} is out (you have {2}). Click for details.", AppInfo.Name, _update.Version, Updates.CurrentText));
                if (!quiet || !UpdateButton.IsVisible) LastLog.Text = L.F("{0} {1} is available", AppInfo.Name, _update.Version);
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

    DateTime _settingsUnlockedUntil;

    /// <summary>
    /// A user who may change this computer's settings (asked again after five minutes). Without the database there are
    /// no users to ask for: Settings opens, so the connection can be fixed.
    /// </summary>
    async Task<bool> MayOpenSettings()
    {
        if (_library.Db is not { } db || DateTime.UtcNow < _settingsUnlockedUntil || _user?.Can(Permission.Computer) == true) return true;
        var login = new LoginWindow(db, AppInfo.Name, Permission.Computer, L.T("Settings of this computer"), _settings.StationName);
        if (await login.ShowDialog<SintonizaUser?>(this) is not { } user) return false;
        AppLog.Write($"Settings opened by {user.Name}");
        _settingsUnlockedUntil = DateTime.UtcNow.AddMinutes(5);
        return true;
    }

    async Task OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        try
        {
            if (!await MayOpenSettings()) return;
            var routingBefore = _settings.Routing;
            string libraryBefore = _settings.Library.ConnectionKey();
            var micBefore = _settings.ActiveMic;
            var settingsWindow = new SettingsWindow(_settings, _engine, this);
            // Its GPIO test opens the port: let go of it meanwhile.
            settingsWindow.GpioTestRequested += testing => { if (testing) StopGpio(); else ApplyGpio(); };
            bool saved = await settingsWindow.ShowDialog<bool>(this);
            ApplyGpio();
            if (!saved) return;
            if (_engine != null)
            {
                if (_settings.Routing != routingBefore) _engine.ApplyRouting(_settings.Routing);
                if (_settings.ActiveMic != micBefore) _engine.ConfigureMic(_settings.ActiveMic);
            }
            if (_settings.Library.ConnectionKey() != libraryBefore)
            {
                _stationVersion = -1; // another database: its station settings
                _ = _library.OpenAsync(_settings.Library);
            }
            ApplyStationSettings();
            ApplyShortcuts();
            ApplyRemote();
            LastLog.Text = L.T("Settings saved");
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    void ApplyStationBranding()
    {
        PlaylistRow.Currency = string.IsNullOrWhiteSpace(_settings.CurrencySign) ? PlaylistRow.SystemCurrency : _settings.CurrencySign.Trim();
        foreach (var row in _rows) row.RefreshCurrency();
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
            ? L.T("AUTO: plays through the playlist with crossfades. Click for ASSISTED.")
            : L.T("ASSISTED: stops after each item; start the next one yourself. Click for AUTO."));
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
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { SaveSettingsNow(); }
        finally { Slow("saving settings", started); }
    }

    void SaveSettingsNow()
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
    readonly TakeService _takes = new();

    /// <summary>The music library (for the settings window).</summary>
    internal LibraryService MusicLibrary => _library;

    // ───────────────────────── Backups ─────────────────────────

    Backups? _backups;

    HealthMonitor? _health;

    void StartBackups()
    {
        if (_engine != null)
        {
            _health = new HealthMonitor(_engine, _library, () => _settings);
            _health.Message += message =>
            {
                AppLog.Write(message);
                Dispatcher.UIThread.Post(() => LastLog.Text = message);
            };
        }
        _backups = new Backups(() => _settings.Backup, () => _library.Db);
        _backups.Failed += error => _health?.Report("backup", L.F("The backup failed: {0}", error));
        _backups.Message += message =>
        {
            AppLog.Write(message);
            Dispatcher.UIThread.Post(() => LastLog.Text = message);
        };
    }

    /// <summary>A backup now (Settings → Safety): its file, or null if it failed.</summary>
    internal Task<string?> BackupNow() => _backups?.RunAsync() ?? Task.FromResult<string?>(null);

    internal FileInfo? LastBackup => _backups?.Last;

    /// <summary>
    /// The categories' folders for the editor: a folder added is put in the library's scan list (if no folder there
    /// covers it already; <paramref name="alsoAdd"/> tells an open Settings window too), and after a change the
    /// settings are saved and the library scans.
    /// </summary>
    internal CategoryFolders CategoryFolders(Action<string>? alsoAdd = null) => new(
        () => _library.Db?.FolderCategories() ?? new(),
        (folder, id) => _library.Db?.SetFolderRule(folder, id),
        folder =>
        {
            bool covered = _settings.Library.Folders.Any(f => Under(folder, f));
            if (!covered) _settings.Library.Folders.Add(folder);
            if (!covered) alsoAdd?.Invoke(folder);
        },
        () =>
        {
            _settings.Save();
            ApplyAutoScan();
            _ = _library.ScanAsync(_settings.Library.Folders, automatic: true);
        });

    /// <summary>
    /// Folders given to categories in this computer's settings (before they were kept in the database): moved into the
    /// database once it is open.
    /// </summary>
    bool _movingFolders;

    void MoveFolderCategories()
    {
        if (_library.Db is not { } db || _settings.Library.FolderCategories.Count == 0) return;
        var rules = new Dictionary<string, long>(_settings.Library.FolderCategories);
        if (_movingFolders) return;
        _movingFolders = true;
        Task.Run(() =>
        {
            try
            {
                var known = db.FolderCategories();
                foreach (var (folder, id) in rules) if (!known.ContainsKey(folder)) db.SetFolderRule(folder, id);
                AppLog.Write($"Category folders moved into the library database: {rules.Count}");
                // Only once they are in the database are they taken out of the settings.
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var folder in rules.Keys) _settings.Library.FolderCategories.Remove(folder);
                    _settings.Save();
                });
            }
            catch (Exception ex) { AppLog.Write($"Category folders not moved into the database: {ex.Message}"); }
            finally { _movingFolders = false; }
        });
    }

    /// <summary>Whether <paramref name="folder"/> is <paramref name="root"/> or inside it.</summary>
    static bool Under(string folder, string root)
    {
        string f = folder.TrimEnd('/', '\\'), r = root.TrimEnd('/', '\\');
        return f.Equals(r, StringComparison.Ordinal) || f.StartsWith(r + "/", StringComparison.Ordinal) || f.StartsWith(r + "\\", StringComparison.Ordinal);
    }

    void ApplyAutoScan()
    {
        var library = _settings.Library;
        _library.ConfigureAutoScan(library.Folders, library.ScanEveryMinutes, library.WatchFolders);
    }

    /// <summary>
    /// Puts a folder's songs in a library category (null = none), and remembers it: new songs found there by a scan
    /// get it too.
    /// </summary>
    async Task SetFolderCategory(string folder, long? id)
    {
        if (_library.Db is not { } db) return;
        string name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        try
        {
            int count = await Task.Run(() =>
            {
                // Out of the category it had (its songs too, also those only also in it), then into the new one.
                int n = db.FolderCategories().TryGetValue(folder, out long before) && before != id ? db.RemoveFolderCategory(folder, before)
                    : id == null ? db.SetFolderCategory(folder, null) : 0;
                if (id != null) n = db.SetFolderCategory(folder, id);
                db.SetFolderRule(folder, id);
                return n;
            });
            string category = id is { } c ? Playout.Library.CategoryTree.PathOf(db.Categories(), c) : "";
            LastLog.Text = count == 0
                ? L.F("No songs from {0} are in the library yet: add the folder in Settings → Library and scan. New songs found there will go in the category.", name)
                : id == null ? L.F("{0}: {1} taken out of their category", name, Plural(count, "{0} song", "{0} songs"))
                : L.F("{0}: {1} put in {2}", name, Plural(count, "{0} song", "{0} songs"), category);
            AppLog.Write(LastLog.Text);
            _library.NotifyChanged();
        }
        catch (Exception ex)
        {
            LastLog.Text = L.F("Category not set: {0}", ex.Message);
        }
    }

    static string Plural(int n, string one, string many) => L.F(n == 1 ? one : many, n);

    /// <summary>The panel beside the playlist: the cartwall, the library (search) or the folders on disk.</summary>
    void ShowSideTab(string tab)
    {
        if (tab is not ("library" or "folders")) tab = "cartwall";
        _settings.SideTab = tab; // saved with the rest every few seconds
        CartwallTab.IsChecked = tab == "cartwall";
        LibraryTab.IsChecked = tab == "library";
        FoldersTab.IsChecked = tab == "folders";
        CartwallPanel.IsVisible = CartwallTools.IsVisible = tab == "cartwall";
        Tracks.IsVisible = ScanButton.IsVisible = tab == "library";
        Library.IsVisible = tab == "folders";
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
        int index = _settings.PlaylistItems.Take(_settings.OnAirIndex + 1).Count(Playable) - 1;
        // In AUTO it plays (as when it closed, or when it was stopped and closed: AUTO is what the station runs on).
        bool play = _settings.ResumeOnStart && _settings.AutoAdvance && (_settings.WasPlaying || _settings.PlaylistItems.Count > 0);
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
        // Before a break that ends at its time, a gap is filled from the station's music even with fill music off;
        // a short one from the Fillers category (beds, sweepers) rather than a song cut after a few seconds.
        _engine.GapFill = new LibraryFill(_library, [], TimeSpan.FromHours(Math.Max(1, _settings.FillSongHours)),
            TimeSpan.FromMinutes(Math.Max(10, _settings.FillArtistMinutes)), null).Next;
        _engine.FillerFill = new LibraryFill(_library, [], TimeSpan.FromMinutes(20), TimeSpan.Zero, null, role: "fillers").Next;
        _engine.FillerGapSeconds = _settings.FillerGapSeconds;
        // After the time signal the new hour starts: songs left from the hour before don't.
        _engine.OldHour = t =>
        {
            var soon = DateTime.Now.AddSeconds(30);
            var hour = new DateTime(soon.Year, soon.Month, soon.Day, soon.Hour, 0, 0);
            return PlanPlayer.ReadTag(t.Tag) is { } tag && tag.Hour < hour
                   && t.Kind == TrackKind.Music && t.Lock == ItemLock.Free && !t.InBreak && t.Event == null;
        };
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
            string message = error == null ? L.T("RDS: sending to the encoder") : L.F("RDS: can't send to the encoder ({0}); trying again", error);
            AppLog.Write(message);
            LastLog.Text = message;
        });
        _rds = rds;
    }

    // ───────────────────────── GPIO ─────────────────────────

    readonly List<IGpioSource> _gpio = new();
    string _gpioKey = "";
    GpioState? _gpioShown;

    /// <summary>Opens (or opens again, with new settings) the GPIO sources that are on: serial port, network, MIDI.</summary>
    void ApplyGpio()
    {
        var g = _settings.Gpio ?? new();
        string key = System.Text.Json.JsonSerializer.Serialize(g);
        if (_gpio.Count > 0 && key == _gpioKey) return;
        StopGpio();
        _gpioKey = key;
        void Start(string what, Func<IGpioSource> open)
        {
            try
            {
                var source = open();
                source.Fired += (action, cart) => Dispatcher.UIThread.Post(() => OnGpio(action, cart));
                _gpio.Add(source);
                AppLog.Write(L.F("GPIO: {0}", what));
            }
            catch (Exception ex)
            {
                string message = L.F("GPIO: {0} can't be opened ({1})", what, ex.Message);
                AppLog.Write(message);
                LastLog.Text = message;
            }
        }
        if (g.Enabled && g.Port.Length > 0) Start(g.Port, () => GpioService.Open(g));
        if (g.Network is { Enabled: true } net) Start(L.F("network, port {0}", net.Port), () => new NetGpio(net));
        if (g.Midi is { Enabled: true } midi && (midi.Input.Length > 0 || midi.Output.Length > 0))
            Start(L.F("MIDI {0}", midi.Input.Length > 0 ? midi.Input : midi.Output), () => new MidiGpio(midi));
    }

    void StopGpio()
    {
        foreach (var source in _gpio) source.Dispose();
        _gpio.Clear();
        _gpioKey = "";
        _gpioShown = null;
    }

    /// <summary>A desk contact closed or opened. Next and Stop aren't done while an advert is on air (as on screen, without asking).</summary>
    void OnGpio(GpioAction action, int cart)
    {
        if (_engine is not { } engine) return;
        var st = engine.GetStatus();
        string what = action == GpioAction.Cart ? L.F("cart {0}", cart) : action.ToString();
        if (action is GpioAction.Next or GpioAction.Stop or GpioAction.PlayPause && AdvertOnAir(st) && st.State == PlayerState.Playing)
        {
            LastLog.Text = L.F("GPIO {0} not done: an advert is on air", what);
            AppLog.Write(LastLog.Text);
            return;
        }
        switch (action)
        {
            case GpioAction.Play: if (st.State != PlayerState.Playing) engine.Play(); break;
            case GpioAction.Next: engine.Next(); break;
            case GpioAction.Stop: engine.Stop(); break;
            case GpioAction.PlayPause: engine.TogglePause(); break;
            case GpioAction.Cart when cart >= 1 && cart <= _page.Carts.Count: ToggleCart(cart - 1); break;
        }
        AppLog.Write(L.F("GPIO: {0}", what));
    }

    /// <summary>The GPIO outputs from what is on air (only when it changes).</summary>
    void ShowGpio(PlayerStatus st)
    {
        if (_gpio.Count == 0) return;
        bool playing = st.State == PlayerState.Playing;
        bool silence = playing && st.Silence >= Math.Max(5, _settings.Silence?.Seconds ?? 15); // as the alerts count it
        var now = new GpioState(playing, AdvertOnAir(st), playing && st.Current?.IsStream == true, silence);
        if (_gpioShown == now) return;
        _gpioShown = now;
        foreach (var source in _gpio) source.Show(now);
    }

    // ───────────────────────── Schedule, reports, website ─────────────────────────

    readonly AsRunLog _asRun = new(AsRunLog.DefaultFolder);
    Scheduler? _scheduler;
    PlanPlayer? _plan;

    /// <summary>How the daily playlists are made: the crossfades and the rest times of fill music.</summary>
    PlaylistGenerator.Options PlanOptions() => new(_settings.Crossfade, _settings.OtherCrossfade,
        TimeSpan.FromHours(_settings.FillSongHours), TimeSpan.FromMinutes(_settings.FillArtistMinutes));
    WebNowPlaying? _web;
    ReportsWindow? _reports;

    void OpenReports()
    {
        if (_reports != null) { _reports.Activate(); return; }
        _reports = new ReportsWindow(AsRunLog.DefaultFolder, _settings.StationName) { Database = () => _library.Db };
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
            string message = error == null ? L.T("Website: now playing sent") : L.F("Website: can't send now playing ({0}); trying again", error);
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
