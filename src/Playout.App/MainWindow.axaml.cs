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
using Playout.Engine;

namespace Playout.App;

public partial class MainWindow : Window
{
    readonly PlayoutEngine? _engine;
    readonly AppSettings _settings = AppSettings.Load();
    readonly ObservableCollection<PlaylistRow> _rows = new();
    readonly Dictionary<Guid, PlaylistRow> _rowsById = new();
    readonly CartSlot[] _carts = new CartSlot[AppSettings.CartCount];
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

    public MainWindow()
    {
        InitializeComponent();
        Playlist.ItemsSource = _rows;

        try
        {
            _engine = new PlayoutEngine(_settings.Routing, ForcedDeviceFromArgs());
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
        WireEvents();

        if (_engine != null)
        {
            _engine.CrossfadeSeconds = _settings.Crossfade;
            _engine.AutoAdvance = _settings.AutoAdvance;
            var existing = _settings.Playlist.Where(File.Exists).ToList();
            if (existing.Count > 0) _engine.AddFiles(existing);
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

        AutoSwitch.IsCheckedChanged += (_, _) =>
        {
            _settings.AutoAdvance = AutoSwitch.IsChecked == true;
            if (_engine != null) _engine.AutoAdvance = _settings.AutoAdvance;
            ShowAutomationMode();
        };

        AddButton.Click += async (_, _) => await AddFilesFromPicker();
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

        StreamToggle.Click += (_, _) => ToggleStreaming();
        SettingsButton.Click += async (_, _) => await OpenSettings();

        KeyDown += OnWindowKeyDown;
        Closing += (_, _) => SaveSettings();
        Closed += (_, _) => _engine?.Dispose();
    }

    /// <summary>Play resumes or continues; when stopped, it starts from the selected item if there is one.</summary>
    void PlayPressed()
    {
        bool stopped = _engine?.State == PlayerState.Stopped;
        _engine?.Play(stopped && Playlist.SelectedItem is PlaylistRow row ? row.Id : null);
    }

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var cmd = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.F5: PlayPressed(); break;
            case Key.F6: _engine?.TogglePause(); break;
            case Key.F7: _engine?.Next(); break;
            case Key.F8: _engine?.Stop(); break;
            case Key.O when e.KeyModifiers == cmd: _ = AddFilesFromPicker(); break;
            case Key.S when e.KeyModifiers == cmd: _ = SavePlaylist(); break;
            case Key.Up when e.KeyModifiers == cmd: MoveSelected(-1); break;
            case Key.Down when e.KeyModifiers == cmd: MoveSelected(+1); break;
            case Key.OemComma when e.KeyModifiers == cmd: _ = OpenSettings(); break;
            default: return;
        }
        e.Handled = true;
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
        if (st.Current is { } cur && _rowsById.TryGetValue(cur.Id, out var onAirRow))
            onAirRow.Progress = st.Length > 0 ? st.Position / st.Length : 0;
        Elapsed.Text = Format.Duration(st.Position);
        double remaining = Math.Max(0, st.Length - st.Position);
        Remaining.Text = "-" + Format.Duration(Math.Ceiling(remaining));
        Remaining.Foreground = hasTrack && remaining <= 10
            ? (IBrush)this.FindResource("OnAir")!
            : (IBrush)this.FindResource("TextMain")!;

        PlayButton.Classes.Set("active", st.State == PlayerState.Playing);
        PauseButton.Classes.Set("active", st.State == PlayerState.Paused);
        StopButton.Classes.Set("active", st.State == PlayerState.Stopped);
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
        double xfade = _engine?.CrossfadeSeconds ?? 0;
        WaveMain.SetCrossfade(xfade);
        WaveOverview.SetCrossfade(xfade);
        WaveMain.UpdatePosition(st.Position, playing);
        WaveOverview.UpdatePosition(st.Position, playing);
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

        // Mirrors the engine: items no longer than two crossfades (jingles, stingers) play out in full.
        double Overlap(double length) => length > xfade * 2 ? xfade : 0;
        double lastOverlap = Overlap(st.Length);

        DateTime? at = st.State == PlayerState.Playing && current >= 0
            ? DateTime.Now.AddSeconds(Math.Max(0, st.Length - st.Position - lastOverlap))
            : null;

        for (int i = 0; i < items.Count; i++)
        {
            if (!_rowsById.TryGetValue(items[i].Id, out var row)) continue;
            if (at is { } t && startFrom >= 0 && i >= startFrom)
            {
                row.StartTime = t.ToString("HH:mm:ss");
                lastOverlap = Overlap(items[i].Duration);
                at = t.AddSeconds(items[i].Duration - lastOverlap);
            }
            else row.StartTime = "";
        }

        // When the last item finishes: the chain above ends one crossfade early; with nothing queued it is the current item.
        if (at is { } end) _playlistEnds = end.AddSeconds(lastOverlap);
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
            StreamState.OnAir => "Streaming — click to stop",
            StreamState.Connecting => "Connecting — click to cancel",
            StreamState.Failed => $"{st.StreamError}\nClick to retry",
            _ => "Stream off — click to start (server is set in Settings)",
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
            row.Number = i + 1;
            row.IsPlaying = t.Id == snap.CurrentId;
            row.IsNext = t.Id == snap.NextId;
            row.IsPlayed = !row.IsPlaying && snap.Played.Contains(t.Id);
            desired.Add(row);
        }
        foreach (var dead in _rowsById.Keys.Where(k => !alive.Contains(k)).ToList()) _rowsById.Remove(dead);

        // In-place sync keeps the ListBox scroll position stable.
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
        double total = snap.Items.Sum(t => t.Duration);
        PlaylistInfo.Text = _rows.Count == 0 ? "" : $"{_rows.Count} items · {Format.Duration(total)}";
        NextTitle.Text = snap.NextId is { } n && _rowsById.TryGetValue(n, out var next) ? next.Track.Display : "—";

        _settings.Playlist = snap.Items.Select(t => t.Path).ToList();
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
        var entries = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => Path.IsPathRooted(l) ? l : Path.GetFullPath(Path.Combine(baseDir, l)))
            .Where(File.Exists);
        _engine?.AddFiles(entries);
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
            lines.Add($"#EXTINF:{(int)t.Duration},{t.Display}");
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

    // ───────────────────────── Cartwall ─────────────────────────

    sealed class CartSlot
    {
        public required Button Button;
        public required TextBlock Name;
        public required TextBlock Time;
        public string? Path;
        public int Handle;
    }

    void BuildCartwall()
    {
        for (int i = 0; i < _carts.Length; i++)
        {
            int index = i;
            var name = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, FontWeight = FontWeight.SemiBold, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            var time = new TextBlock { FontSize = 12, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Right };
            time.Classes.Add("digits");
            var number = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Opacity = 0.5 };

            var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumnSpan(name, 2);
            Grid.SetRow(number, 1);
            Grid.SetRow(time, 1);
            Grid.SetColumn(time, 1);
            content.Children.Add(name);
            content.Children.Add(number);
            content.Children.Add(time);

            var button = new Button { Content = content };
            button.Classes.Add("cart");
            button.Click += (_, _) => ToggleCart(index);

            var assign = new MenuItem { Header = "Assign file…" };
            assign.Click += async (_, _) => await AssignCart(index);
            var clear = new MenuItem { Header = "Clear" };
            clear.Click += (_, _) => SetCart(index, null);
            button.ContextMenu = new ContextMenu { Items = { assign, clear } };

            _carts[i] = new CartSlot { Button = button, Name = name, Time = time };
            Cartwall.Children.Add(button);
            SetCart(i, _settings.Carts[i]);
        }
    }

    void SetCart(int index, string? path)
    {
        var slot = _carts[index];
        if (slot.Handle != 0) { _engine?.StopCart(slot.Handle); slot.Handle = 0; }
        slot.Path = path;
        _settings.Carts[index] = path;
        slot.Name.Text = path == null ? "" : Path.GetFileNameWithoutExtension(path);
        slot.Time.Text = "";
        slot.Button.Classes.Set("empty", path == null);
        ToolTip.SetTip(slot.Button, path);
    }

    async Task AssignCart(int index)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Assign cart {index + 1}",
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = AudioFiles.Patterns }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SetCart(index, path);
    }

    void ToggleCart(int index)
    {
        var slot = _carts[index];
        if (_engine == null || slot.Path == null) return;
        if (slot.Handle != 0)
        {
            _engine.StopCart(slot.Handle);
            slot.Handle = 0;
        }
        else
        {
            slot.Handle = _engine.FireCart(slot.Path);
        }
    }

    void UpdateCarts()
    {
        foreach (var slot in _carts)
        {
            double? left = slot.Handle != 0 ? _engine?.CartRemaining(slot.Handle) : null;
            if (left == null) slot.Handle = 0;
            slot.Button.Classes.Set("playing", left != null);
            slot.Time.Text = left is { } s ? "-" + Format.Duration(Math.Ceiling(s)) : "";
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
            bool saved = await new SettingsWindow(_settings, _engine).ShowDialog<bool>(this);
            if (!saved) return;
            if (_engine != null)
            {
                _engine.CrossfadeSeconds = _settings.Crossfade;
                if (_settings.Routing != routingBefore) _engine.ApplyRouting(_settings.Routing);
            }
            ApplyStationBranding();
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

    void LoadSettingsIntoUi()
    {
        AutoSwitch.IsChecked = _settings.AutoAdvance;
        ShowAutomationMode();
        Library.SetRoot(_settings.MusicFolder);
        ApplyStationBranding();
    }

    void SaveSettings() => _settings.Save();
}
