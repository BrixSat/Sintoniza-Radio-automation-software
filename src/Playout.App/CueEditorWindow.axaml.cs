using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Playout.App.Controls;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sets the cue points of one file: pre-listen on the Preview output, then mark Cue In, Intro, Outro, Mix and
/// Cue Out at the playhead or type the times. Saved per file, so they apply wherever the file is used.
/// </summary>
public partial class CueEditorWindow : Window
{
    enum Marker { CueIn, Intro, Outro, Mix, CueOut }

    static readonly (Marker Marker, string Name, string Color, string Help)[] Rows =
    [
        (Marker.CueIn, L.T("Cue In"), "#E7E9EE", L.T("Playback starts here. Skips silence or a count-in at the start.")),
        (Marker.Intro, L.T("Intro"), "#3ECF8E", L.T("End of the intro (where the vocals start). Talk-over countdown on air.")),
        (Marker.Outro, L.T("Outro"), "#B392F0", L.T("Start of the ending. Countdown to the end from here, to talk over the close.")),
        (Marker.Mix, L.T("Mix"), "#F5A524", L.T("The next item starts here and this one plays on to Cue Out. Empty = normal crossfade.")),
        (Marker.CueOut, L.T("Cue Out"), "#E7E9EE", L.T("Playback stops here. Cuts silence or a long fade at the end.")),
    ];

    /// <summary>The ▶ button next to a marker starts this far before it, so you hear the lead-in.</summary>
    const double PreRoll = 3;

    readonly SegueHost _host = null!;
    readonly string _path = "";
    readonly PreviewPlayer? _preview;
    readonly double _length;
    readonly double?[] _values = new double?[Rows.Length];
    readonly TextBox[] _boxes = new TextBox[Rows.Length];
    readonly DispatcherTimer? _timer;
    Waveform? _wave;

    /// <summary>The cue points were saved.</summary>
    public bool Saved { get; private set; }

    // Parameterless constructor for the XAML previewer.
    public CueEditorWindow() => InitializeComponent();

    public CueEditorWindow(PlayoutEngine engine, string path, string title) : this(SegueHost.For(engine), path, title) { }

    /// <summary>With what opens, reads and saves the file's cue points: the playout's, or Sintoniza Pro's (the library).</summary>
    public CueEditorWindow(SegueHost host, string path, string title)
    {
        InitializeComponent();
        Localization.Apply(this);
        _host = host;
        _path = path;
        Title = L.F("Cue points — {0}", title);
        TitleText.Text = title;
        PathText.Text = path;
        OnAirWarning.IsVisible = host.PreviewIsOnAir;

        Load(host.GetCues(path));
        BuildMarkerRows();

        _preview = host.Open(path);
        _length = _preview?.Length ?? 0;
        if (_preview == null)
        {
            ErrorText.Text = L.T("This file cannot be opened.");
            SaveButton.IsEnabled = false;
        }

        host.LoadWaveform(path).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _wave = t.IsCompletedSuccessfully ? t.Result : null;
            WaveZoom.SetTrack(_wave, _length);
            WaveFull.SetTrack(_wave, _length);
            AutoButton.IsEnabled = _wave != null;
        }));

        WaveZoom.SeekRequested += Seek;
        WaveFull.SeekRequested += Seek;
        PlayButton.Click += (_, _) => TogglePlay();
        Back5Button.Click += (_, _) => Nudge(-5);
        Back1Button.Click += (_, _) => Nudge(-1);
        Fwd1Button.Click += (_, _) => Nudge(+1);
        Fwd5Button.Click += (_, _) => Nudge(+5);

        AutoButton.Click += (_, _) =>
        {
            if (_wave == null) return;
            Load(CueDetector.Detect(_wave, Current()));
            Refresh();
        };
        ClearAllButton.Click += (_, _) =>
        {
            Array.Clear(_values);
            Refresh();
        };
        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += async (_, _) =>
        {
            CommitFocusedBox();
            if (Validate() != null) return;
            SaveButton.IsEnabled = false;
            try
            {
                await _host.SaveCues(_path, Current());
                Saved = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                SaveButton.IsEnabled = true;
            }
        };

        // Tunnel, so Space and the arrows work before buttons or lists take them (but not while typing a time).
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _preview?.Dispose();
        };

        Refresh();
        Tick();
    }

    // ───────────────────────── Markers ─────────────────────────

    void Load(CuePoints c)
    {
        _values[(int)Marker.CueIn] = c.CueIn;
        _values[(int)Marker.Intro] = c.Intro;
        _values[(int)Marker.Outro] = c.Outro;
        _values[(int)Marker.Mix] = c.Mix;
        _values[(int)Marker.CueOut] = c.CueOut;
    }

    CuePoints Current() => new(
        _values[(int)Marker.CueIn], _values[(int)Marker.Intro], _values[(int)Marker.Outro],
        _values[(int)Marker.Mix], _values[(int)Marker.CueOut]);

    void BuildMarkerRows()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            int index = i;
            var (_, name, color, help) = Rows[i];
            var brush = new SolidColorBrush(Color.Parse(color));

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,80,100,8,Auto,6,Auto,6,Auto,16,*") };
            Add(grid, 0, new Ellipse { Width = 9, Height = 9, Fill = brush, VerticalAlignment = VerticalAlignment.Center });
            Add(grid, 1, new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });

            var box = new TextBox { PlaceholderText = L.T("not set"), VerticalAlignment = VerticalAlignment.Center };
            box.Classes.Add("time");
            box.LostFocus += (_, _) => Commit(index);
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                Commit(index);
                e.Handled = true;
            };
            _boxes[i] = box;
            Add(grid, 2, box);

            var set = SmallButton(L.T("Set"), L.F("Put {0} at the playhead", name));
            set.Click += (_, _) =>
            {
                if (_preview == null) return;
                _values[index] = Math.Round(_preview.Position, 2);
                Refresh();
            };
            Add(grid, 4, set);

            var play = SmallButton("▶", Rows[i].Marker == Marker.CueIn ? L.T("Play from here") : L.F("Play from {0:0} s before", PreRoll));
            play.Click += (_, _) =>
            {
                if (_preview == null || _values[index] is not { } at) return;
                Seek(Rows[index].Marker == Marker.CueIn ? at : at - PreRoll);
                _preview.Play();
            };
            Add(grid, 6, play);

            var clear = SmallButton("✕", L.T("Clear"));
            clear.Click += (_, _) =>
            {
                _values[index] = null;
                Refresh();
            };
            Add(grid, 8, clear);

            Add(grid, 10, new TextBlock { Text = help, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Classes = { "note" } });
            MarkerRows.Children.Add(grid);
        }

        static void Add(Grid grid, int column, Control child)
        {
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        static Button SmallButton(string text, string tip)
        {
            var b = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
            b.Classes.Add("small");
            ToolTip.SetTip(b, tip);
            return b;
        }
    }

    /// <summary>Takes a typed time; an empty box clears the marker, anything unreadable puts the old value back.</summary>
    void Commit(int index)
    {
        var text = _boxes[index].Text;
        if (string.IsNullOrWhiteSpace(text)) _values[index] = null;
        else if (Format.ParseCueTime(text) is { } t) _values[index] = Math.Round(t, 2);
        Refresh(force: true);
    }

    void CommitFocusedBox()
    {
        int i = Array.FindIndex(_boxes, b => b.IsFocused);
        if (i >= 0) Commit(i);
    }

    /// <summary>Redraws the markers, shows the times and checks the order.</summary>
    void Refresh(bool force = false)
    {
        for (int i = 0; i < Rows.Length; i++)
            if (force || !_boxes[i].IsFocused)
                _boxes[i].Text = _values[i] is { } v ? Format.CueTime(v) : "";

        var preview = new Track(Guid.Empty, _path, "", "", _length, Cues: Current());
        var markers = CueMarkers.For(preview, _host.Crossfade(preview.Kind, preview.Kind));
        WaveZoom.SetMarkers(markers);
        WaveFull.SetMarkers(markers);

        var error = Validate();
        ErrorText.Text = error ?? "";
        SaveButton.IsEnabled = error == null && _preview != null;
    }

    string? Validate()
    {
        if (_preview == null) return L.T("This file cannot be opened.");
        for (int i = 0; i < Rows.Length; i++)
            if (_values[i] > _length + 0.05) return L.F("{0} is past the end of the file ({1}).", Rows[i].Name, Format.CueTime(_length));

        double cueIn = _values[(int)Marker.CueIn] ?? 0;
        double cueOut = _values[(int)Marker.CueOut] ?? _length;
        if (cueIn >= cueOut) return L.T("Cue In must be before Cue Out.");
        if (_values[(int)Marker.Intro] is { } intro && (intro <= cueIn || intro >= cueOut)) return L.T("Intro must be between Cue In and Cue Out.");
        if (_values[(int)Marker.Outro] is { } outro && (outro <= cueIn || outro >= cueOut)) return L.T("Outro must be between Cue In and Cue Out.");
        if (_values[(int)Marker.Intro] >= _values[(int)Marker.Outro]) return L.T("Intro must be before Outro.");
        if (_values[(int)Marker.Mix] is { } mix && (mix < cueIn || mix > cueOut)) return L.T("Mix must be between Cue In and Cue Out.");
        return null;
    }

    // ───────────────────────── Pre-listen ─────────────────────────

    void TogglePlay()
    {
        if (_preview == null) return;
        if (_preview.IsPlaying) _preview.Pause();
        else _preview.Play();
    }

    void Seek(double seconds)
    {
        if (_preview == null) return;
        seconds = Math.Clamp(seconds, 0, _length);
        _preview.Seek(seconds);
        // Show the jump at once; the device buffer takes a moment to catch up.
        WaveZoom.UpdatePosition(seconds, false);
        WaveFull.UpdatePosition(seconds, false);
    }

    void Nudge(double seconds)
    {
        if (_preview != null) Seek(_preview.Position + seconds);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1;
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: Nudge(-step); break;
            case Key.Right: Nudge(step); break;
            default: return;
        }
        e.Handled = true;
    }

    void Tick()
    {
        if (_preview == null) return;
        double pos = _preview.Position;
        bool playing = _preview.IsPlaying;
        WaveZoom.UpdatePosition(pos, playing);
        WaveFull.UpdatePosition(pos, playing);
        PositionText.Text = $"{Format.CueTime(pos)} / {Format.CueTime(_length)}";
        PlayIcon.Data = (Geometry)this.FindResource(playing ? "IconPause" : "IconPlay")!;
    }
}
