using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// The hour mix editor (Sintoniza Pro → Daily playlists): a whole hour on one time line, every item where it starts.
/// An item can be made to start earlier, over the end of the one before (this hour only); a join opens in the segue
/// editor for its cue points (every time they play); and the hour can be heard from anywhere, through its joins, on
/// the Preview output. Returns true when the hour was saved.
/// </summary>
public partial class HourMixWindow : Window
{
    readonly SegueHost _host = null!;
    readonly List<MixBlock> _blocks = new();
    readonly Func<string, double> _fileLength = null!;
    readonly Func<IReadOnlyList<PlanItem>, Task<bool>> _save = null!;
    HourMix? _mix;
    bool _changed, _closing;

    // Parameterless constructor for the XAML previewer.
    public HourMixWindow() => InitializeComponent();

    /// <param name="blocks">The hour's items in order, with copies of the daily playlist's items (changed here, saved by <paramref name="save"/>).</param>
    /// <param name="fileLength">A file's length in seconds (0: not known); asked in the background.</param>
    public HourMixWindow(SegueHost host, DateTime hour, IEnumerable<MixBlock> blocks, Func<string, double> fileLength, Func<IReadOnlyList<PlanItem>, Task<bool>> save)
    {
        InitializeComponent();
        Localization.Apply(this);
        _host = host;
        _fileLength = fileLength;
        _save = save;
        _blocks.AddRange(blocks);
        Title = L.F("Mix editor — {0:dddd d MMMM, HH}:00", hour);
        HeadingText.Text = L.F("{0:dddd d MMMM}, {0:HH}:00 to {1:HH}:00", hour, hour.AddHours(1));
        OnAirWarning.IsVisible = host.PreviewIsOnAir;
        MovePanel.IsEnabled = false;
        SaveButton.IsEnabled = false;
        ErrorText.Text = L.T("Reading the files…");

        Timeline.Dragged = (i, by) => Changed(_mix?.Move(i, by) ?? 0, replay: false); // heard again on the next Play
        Timeline.SelectionChanged += _ => ShowSelected();
        Timeline.JoinOpened += async i => await EditJoin(i);
        Timeline.ViewChanged += ShowScroll;
        Scroll.Scroll += (_, _) => Timeline.ScrollTo(Scroll.Value);
        ZoomInButton.Click += (_, _) => Timeline.Zoom(1.5);
        ZoomOutButton.Click += (_, _) => Timeline.Zoom(1 / 1.5);
        ZoomAllButton.Click += (_, _) => Timeline.ZoomAll();
        Earlier1.Click += (_, _) => Move(Timeline.SelectedIndex, -1);
        Earlier01.Click += (_, _) => Move(Timeline.SelectedIndex, -0.1);
        Later01.Click += (_, _) => Move(Timeline.SelectedIndex, 0.1);
        Later1.Click += (_, _) => Move(Timeline.SelectedIndex, 1);
        AutomaticButton.Click += (_, _) => Changed(_mix?.MakeAutomatic(Timeline.SelectedIndex) ?? 0);
        SegueButton.Click += async (_, _) => await EditJoin(Timeline.SelectedIndex);
        PlayButton.Click += (_, _) => Play();
        StopButton.Click += (_, _) => StopListening();
        CancelButton.Click += (_, _) => Close(false);
        // Cancel, Esc or the window's own close: ask first when something changed.
        Closing += async (_, e) =>
        {
            if (!_changed || _closing) return;
            e.Cancel = true;
            if (!await Dialogs.Confirm(this, L.T("Mix editor"), L.T("Close without saving the changes to this hour?"), L.T("Close"))) return;
            _closing = true;
            Close(false);
        };
        SaveButton.Click += async (_, _) => await Save();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space || e.Source is TextBox) return;
            e.Handled = true;
            if (_playing.Count > 0) StopListening(); else Play();
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) => StopListening();
        Load().Report();
    }

    /// <summary>Each file's cue points, length and waveform (in the background), then the time line.</summary>
    async Task Load()
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var b in _blocks)
                {
                    b.Cues = _host.GetCues(b.Path);
                    b.FileLength = _fileLength(b.Path);
                }
            });
            _mix = new HourMix(_blocks, _host.Crossfade);
            _mix.Normalize(); // where the playout starts each item, with the cue points as they are now
            Timeline.SetMix(_mix);
            Timeline.ZoomAll();
            ShowScroll();
            Timeline.Select(_blocks.Count > 1 ? 1 : 0);
            ErrorText.Text = "";
            ShowEnds();
            foreach (var b in _blocks)
            {
                b.Wave = await _host.LoadWaveform(b.Path);
                Timeline.InvalidateVisual();
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Can't read the hour: {0}", ex.Message);
        }
    }

    void Move(int i, double by) => Changed(_mix?.Move(i, by) ?? 0);

    double Changed(double moved, bool replay = true)
    {
        if (Math.Abs(moved) < 0.001) return 0;
        _changed = true;
        SaveButton.IsEnabled = true;
        if (replay && _playing.Count > 0) Play(); // hear the change at once
        Timeline.InvalidateVisual();
        ShowSelected();
        ShowEnds();
        ShowScroll();
        return moved;
    }

    void ShowEnds()
    {
        if (_mix == null) return;
        double ends = _mix.Ends, off = ends - 3600;
        EndsText.Text = Math.Abs(off) < 1
            ? L.F("The hour ends at {0}, on the top of the hour.", Clock(ends))
            : off < 0
                ? L.F("The hour ends at {0}, {1} before the top of the hour.", Clock(ends), Clock(-off))
                : L.F("The hour ends at {0}, {1} past the top of the hour (the playout leaves out what doesn't fit).", Clock(ends), Clock(off));
    }

    static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Abs(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    void ShowSelected()
    {
        int i = Timeline.SelectedIndex;
        if (_mix == null || i < 0 || i >= _blocks.Count)
        {
            SelectedTitle.Text = SelectedText.Text = "";
            MovePanel.IsEnabled = false;
            return;
        }
        var block = _blocks[i];
        SelectedTitle.Text = L.F("{0}  ·  starts at {1}", block.Title, Clock(block.Start));
        bool movable = _mix.Movable(i);
        MovePanel.IsEnabled = movable;
        AutomaticButton.IsEnabled = movable && _mix.ByHand(i);
        if (block.Item == null) SelectedText.Text = L.T("From the schedule (an ad break or an event): it doesn't move here, and on air it goes in at its own time, so its place here is close, not exact.");
        else if (block.Item.FollowsPrevious || block.Kind == TrackKind.Voice) SelectedText.Text = L.T("A voice track: where it starts over its song is set in Voice track….");
        else if (i == 0) SelectedText.Text = L.T("The hour's first item.");
        else if (!movable) SelectedText.Text = L.T("After something from the schedule: it starts when that ends.");
        else
        {
            string before = _blocks[i - 1].Title;
            double overlap = _mix.Overlap(i);
            SelectedText.Text = _mix.ByHand(i)
                ? L.F("Starts {0:0.0} s before {1} ends, set for this hour; {1} plays on to its end under it.", overlap, before)
                : overlap > 0.05
                    ? L.F("Starts {0:0.0} s before {1} ends: the automatic crossfade.", overlap, before)
                    : L.F("Starts when {0} ends.", before);
        }
    }

    void ShowScroll()
    {
        Scroll.Minimum = 0;
        Scroll.Maximum = Math.Max(0, Timeline.Total - Timeline.Visible);
        Scroll.ViewportSize = Timeline.Visible;
        Scroll.Value = Timeline.Offset;
    }

    /// <summary>The segue editor for an item and the one before it; their cue points apply every time they play.</summary>
    async Task EditJoin(int i)
    {
        if (_mix == null || i <= 0 || i >= _blocks.Count) return;
        var first = _blocks[i - 1];
        var next = _blocks[i];
        // Two files: not a stream, nor a break or event with nothing to play.
        if (first.Path.Length == 0 || next.Path.Length == 0 || PlayoutEngine.IsStreamSource(first.Path) || PlayoutEngine.IsStreamSource(next.Path)) return;
        StopListening();
        if (!await new SegueEditorWindow(_host, new SegueItem(first.Path, first.Title, first.Kind), new SegueItem(next.Path, next.Title, next.Kind))
                .ShowDialog<bool>(this)) return;
        first.Cues = _host.GetCues(first.Path);
        next.Cues = _host.GetCues(next.Path);
        // The joins as the new cue points make them (those set for this hour keep their overlap).
        _mix.Normalize();
        _changed = true;
        SaveButton.IsEnabled = true;
        Timeline.InvalidateVisual();
        ShowSelected();
        ShowEnds();
    }

    // ───────────────────────── Listening ─────────────────────────

    /// <summary>The players of the items being heard, by block (opened a few ahead, freed behind).</summary>
    readonly Dictionary<int, PreviewPlayer> _playing = new();
    DispatcherTimer? _timer;
    int _joinedUpTo = -1;

    /// <summary>From the cursor, through every join as on air: each item starts at its place, over the one before.</summary>
    void Play()
    {
        if (_mix == null) return;
        double from = Timeline.CursorAt;
        StopListening();
        int i = _mix.At(from);
        while (i >= 0 && i < _blocks.Count && Player(i) == null) i++;
        if (i < 0 || i >= _blocks.Count) return;
        var first = Player(i)!;
        first.Seek((_blocks[i].Cues.CueIn ?? 0) + Math.Clamp(from - _blocks[i].Start, 0, Math.Max(0, _blocks[i].Length - 0.5)));
        _joinedUpTo = i;
        JoinAhead();
        first.Play();
        // The cursor in an overlap: the later items already playing then start at once, at their place (their joins,
        // already gone by, wouldn't start them).
        for (int k = i + 1; k < _blocks.Count && _blocks[k].Start <= from; k++)
            if (Player(k) is { } over)
            {
                over.Seek((_blocks[k].Cues.CueIn ?? 0) + (from - _blocks[k].Start));
                over.Play();
            }
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
        PlayButton.Content = L.T("▶  Again");
    }

    /// <summary>Blocks that can't be heard here (a stream, a break with nothing to play, a file not found): passed over.</summary>
    readonly HashSet<int> _silent = new();

    PreviewPlayer? Player(int i)
    {
        if (_playing.TryGetValue(i, out var player)) return player;
        if (_silent.Contains(i)) return null;
        var block = _blocks[i];
        player = block.Path.Length == 0 || PlayoutEngine.IsStreamSource(block.Path) ? null : _host.Open(block.Path);
        if (player == null)
        {
            _silent.Add(i);
            if (block.Path.Length > 0) ErrorText.Text = L.F("{0} can't be opened.", block.Title);
            return null;
        }
        player.Seek(block.Cues.CueIn ?? 0);
        _playing[i] = player;
        return player;
    }

    /// <summary>The next two joins set up ahead of what is heard (over anything that can't be heard here).</summary>
    void JoinAhead()
    {
        int heard = _playing.Where(p => p.Value.IsPlaying).Select(p => p.Key).DefaultIfEmpty(_joinedUpTo).Max();
        while (_joinedUpTo >= 0 && _joinedUpTo < Math.Min(heard + 2, _blocks.Count - 1))
        {
            int i = _joinedUpTo;
            int j = i + 1;
            while (j < _blocks.Count && Player(j) == null) j++;
            if (Player(i) is not { } a || j >= _blocks.Count)
            {
                _joinedUpTo = _blocks.Count; // nothing more to join
                break;
            }
            var b = Player(j)!;
            var block = _blocks[i];
            double start = block.Cues.CueIn ?? 0, end = Math.Min(start + block.Length, a.Length);
            double at = start + _blocks[j].Start - block.Start;
            // As the engine does: a Mix marker or an overlap set for the hour plays on to its end; otherwise it fades out.
            bool playsOn = block.Cues.Mix != null || (j == i + 1 && _blocks[j].Item?.Overlap > 0);
            // Just before the very end, so the join is reached even when it starts as this one ends.
            a.Join(Math.Min(at, end - 0.02), b, playsOn ? null : Math.Max(0, end - at), end);
            _joinedUpTo = j;
        }
    }

    void Tick()
    {
        var heard = _playing.Where(p => p.Value.IsPlaying).Select(p => p.Key).ToList();
        if (heard.Count == 0)
        {
            StopListening();
            return;
        }
        int last = heard.Max();
        var block = _blocks[last];
        Timeline.SetPlayhead(block.Start + _playing[last].Position - (block.Cues.CueIn ?? 0));
        JoinAhead();
        // Those that finished: let go of their files.
        foreach (var (i, player) in _playing.Where(p => p.Key < last - 1 && !p.Value.IsPlaying).ToList())
        {
            player.Dispose();
            _playing.Remove(i);
        }
    }

    void StopListening()
    {
        _timer?.Stop();
        foreach (var player in _playing.Values)
        {
            player.Pause();
            player.ClearJoin();
            player.Dispose();
        }
        _playing.Clear();
        _silent.Clear();
        _joinedUpTo = -1;
        Timeline.SetPlayhead(null);
        PlayButton.Content = L.T("▶  Play from the cursor");
    }

    // ───────────────────────── Saving ─────────────────────────

    async Task Save()
    {
        StopListening();
        if (!_changed) { _closing = true; Close(false); return; }
        SaveButton.IsEnabled = false;
        if (await _save(_blocks.Where(b => b.Item != null).Select(b => b.Item!).ToList()))
        {
            _closing = true;
            Close(true);
        }
        else
        {
            ErrorText.Text = L.T("Not saved (the reason is in the daily playlists' status line).");
            SaveButton.IsEnabled = true;
        }
    }
}
