using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>An item of a join: its file, what to call it and what it is (the crossfade depends on it).</summary>
public sealed record SegueItem(string Path, string Title, TrackKind Kind);

/// <summary>What the segue editor needs from the program that opens it (the playout, or Sintoniza Pro).</summary>
public sealed class SegueHost
{
    /// <summary>Opens a file on the Preview output, paused at the start (null: it can't be read).</summary>
    public required Func<string, PreviewPlayer?> Open { get; init; }
    public required Func<string, CuePoints> GetCues { get; init; }
    /// <summary>Saves a file's cue points (to the library, so every studio has them).</summary>
    public required Func<string, CuePoints, Task> SaveCues { get; init; }
    public required Func<string, Task<Waveform?>> LoadWaveform { get; init; }
    /// <summary>The crossfade between two kinds of item (song to song, or with a jingle or advert).</summary>
    public required Func<TrackKind, TrackKind, double> Crossfade { get; init; }
    public bool PreviewIsOnAir { get; init; }

    /// <summary>The playout's: its Preview output, and cue points saved to the library through it.</summary>
    public static SegueHost For(PlayoutEngine engine) => new()
    {
        Open = engine.OpenPreview,
        GetCues = engine.GetCues,
        SaveCues = (path, cues) => { engine.SetCues(path, cues); return Task.CompletedTask; },
        LoadWaveform = engine.LoadWaveformAsync,
        Crossfade = (a, b) => engine.CrossfadeBetween(new Track(Guid.Empty, "", "", "", 0, a), new Track(Guid.Empty, "", "", "", 0, b)),
        PreviewIsOnAir = engine.PreviewIsOnAir,
    };
}

/// <summary>
/// The segue editor: how one item goes into the next. The end of the first and the start of the next on one time
/// line, joined at the first one's Mix point; slide either, or nudge its Mix, Cue Out and the next one's Cue In, and
/// hear the join as on air (on the Preview output). Saved as the two files' cue points.
/// </summary>
public partial class SegueEditorWindow : Window
{
    static readonly double[] PreRolls = [3, 6, 10, 20];

    readonly SegueHost _host = null!;
    readonly SegueItem _first = null!, _next = null!;
    readonly PreviewPlayer? _firstPlayer, _nextPlayer;
    readonly double _firstLength, _nextLength, _crossfade;
    readonly CuePoints _firstSaved = CuePoints.None, _nextSaved = CuePoints.None;
    CuePoints _firstCues = CuePoints.None, _nextCues = CuePoints.None;
    readonly DispatcherTimer? _timer;
    bool _joinPlaying;

    // Parameterless constructor for the XAML previewer.
    public SegueEditorWindow() => InitializeComponent();

    public SegueEditorWindow(SegueHost host, SegueItem first, SegueItem next)
    {
        InitializeComponent();
        Localization.Apply(this);
        _host = host;
        _first = first;
        _next = next;
        Title = L.F("Segue — {0} → {1}", first.Title, next.Title);
        FirstTitle.Text = first.Title;
        NextTitle.Text = "→  " + next.Title;
        FirstCaption.Text = L.F("THE END OF {0}", first.Title.ToUpperInvariant());
        NextCaption.Text = L.F("THE START OF {0}", next.Title.ToUpperInvariant());
        OnAirWarning.IsVisible = host.PreviewIsOnAir;

        _firstCues = _firstSaved = host.GetCues(first.Path);
        _nextCues = _nextSaved = host.GetCues(next.Path);
        _crossfade = host.Crossfade(first.Kind, next.Kind);
        _firstPlayer = host.Open(first.Path);
        _nextPlayer = host.Open(next.Path);
        _firstLength = _firstPlayer?.Length ?? 0;
        _nextLength = _nextPlayer?.Length ?? 0;

        Task.WhenAll(host.LoadWaveform(first.Path), host.LoadWaveform(next.Path)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (t.IsCompletedSuccessfully) Join.SetWaves(t.Result[0], _firstLength, t.Result[1], _nextLength);
        }));

        PreRollBox.ItemsSource = PreRolls.Select(s => L.F("from {0:0} s before", s)).ToList();
        PreRollBox.SelectedIndex = 1;

        Join.Slid += (first, by) => { if (first) MoveMix(by); else MoveCueIn(by); };
        MixBack1.Click += (_, _) => MoveMix(-1);
        MixBack01.Click += (_, _) => MoveMix(-0.1);
        MixFwd01.Click += (_, _) => MoveMix(0.1);
        MixFwd1.Click += (_, _) => MoveMix(1);
        MixAutoButton.Click += (_, _) => Change(() => _firstCues = _firstCues with { Mix = null });
        OutBack1.Click += (_, _) => MoveCueOut(-1);
        OutBack01.Click += (_, _) => MoveCueOut(-0.1);
        OutFwd01.Click += (_, _) => MoveCueOut(0.1);
        OutFwd1.Click += (_, _) => MoveCueOut(1);
        OutEndButton.Click += (_, _) => Change(() => _firstCues = _firstCues with { CueOut = null });
        InBack1.Click += (_, _) => MoveCueIn(-1);
        InBack01.Click += (_, _) => MoveCueIn(-0.1);
        InFwd01.Click += (_, _) => MoveCueIn(0.1);
        InFwd1.Click += (_, _) => MoveCueIn(1);
        InStartButton.Click += (_, _) => Change(() => _nextCues = _nextCues with { CueIn = null });

        PlayButton.Click += (_, _) => PlayJoin();
        StopButton.Click += (_, _) => StopListening();
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += async (_, _) => await Save();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space) return;
            e.Handled = true;
            if (_joinPlaying) StopListening(); else PlayJoin();
        }, RoutingStrategies.Tunnel);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
        Closed += (_, _) =>
        {
            _timer.Stop();
            _firstPlayer?.Dispose();
            _nextPlayer?.Dispose();
        };
        if (_firstPlayer == null || _nextPlayer == null)
        {
            ErrorText.Text = L.F("{0} can't be opened.", _firstPlayer == null ? first.Title : next.Title);
            PlayButton.IsEnabled = SaveButton.IsEnabled = false;
        }
        Refresh();
    }

    Track FirstTrack => new(Guid.Empty, _first.Path, "", "", _firstLength, _first.Kind, _firstCues.IsEmpty ? null : _firstCues);
    Track NextTrack => new(Guid.Empty, _next.Path, "", "", _nextLength, _next.Kind, _nextCues.IsEmpty ? null : _nextCues);

    /// <summary>Where the next one starts in the first (as the playout works it out), and how the first one leaves.</summary>
    (double Mix, double End, double? Fade) FirstJoin()
    {
        var t = FirstTrack;
        double end = t.End, mix = Math.Min(t.MixPoint(_crossfade), end);
        // A Mix marker: it plays on unfaded to Cue Out; else it fades out from the join to Cue Out.
        double? fade = _firstCues.Mix != null ? null : end - mix;
        return (mix, end, fade);
    }

    static double Round(double s) => Math.Round(s, 2);
    static double? NullIfZero(double s) => s < 0.005 ? null : s;

    void MoveMix(double by)
    {
        var t = FirstTrack;
        Change(() => _firstCues = _firstCues with { Mix = Round(Math.Clamp(FirstJoin().Mix + by, t.Start, t.End)) });
    }

    void MoveCueOut(double by)
    {
        var t = FirstTrack;
        double min = Math.Max(t.Start + 1, (_firstCues.Mix ?? t.Start) + 0.01);
        double value = Round(Math.Clamp(t.End + by, min, _firstLength));
        Change(() => _firstCues = _firstCues with { CueOut = value >= _firstLength - 0.005 ? null : value });
    }

    void MoveCueIn(double by)
    {
        var t = NextTrack;
        // Not past its own Intro, Mix or Cue Out.
        double max = new[] { _nextCues.Intro, _nextCues.Outro, _nextCues.Mix, t.End - 1 }.Where(v => v != null).Min(v => v!.Value) - 0.01;
        Change(() => _nextCues = _nextCues with { CueIn = NullIfZero(Round(Math.Clamp(t.Start + by, 0, Math.Max(0, max)))) });
    }

    void Change(Action change)
    {
        bool replay = _joinPlaying;
        change();
        Refresh();
        if (replay) PlayJoin(); // hear the change at once
    }

    void Refresh()
    {
        var (mix, end, fade) = FirstJoin();
        var next = NextTrack;
        Join.SetJoin(mix, end, fade, next.Start, _nextCues.Intro);
        MixText.Text = Format.CueTime(mix);
        OutText.Text = Format.CueTime(end);
        InText.Text = Format.CueTime(next.Start);
        MixAutoButton.IsEnabled = _firstCues.Mix != null;
        OutEndButton.IsEnabled = _firstCues.CueOut != null;
        InStartButton.IsEnabled = _nextCues.CueIn != null;
        IntroText.Text = _nextCues.Intro is { } intro
            ? L.F("Its intro lasts {0:0.0} s from Cue In (talk-over).", intro - next.Start)
            : L.T("No Intro marker (set it in Edit cue points).");
        double overlap = end - mix;
        SummaryText.Text = overlap < 0.05
            ? L.F("{0} starts when {1} ends (no overlap).", _next.Title, _first.Title)
            : fade != null
                ? L.F("{0} starts {1:0.0} s before {2} ends; {2} fades out over them (the automatic crossfade).", _next.Title, overlap, _first.Title)
                : L.F("{0} starts {1:0.0} s before {2} ends; {2} plays on at full level to its Cue Out.", _next.Title, overlap, _first.Title);
    }

    // ───────────────────────── Listening ─────────────────────────

    void PlayJoin()
    {
        if (_firstPlayer is not { } a || _nextPlayer is not { } b) return;
        StopListening();
        var (mix, end, fade) = FirstJoin();
        b.Seek(NextTrack.Start);
        double preRoll = PreRolls[Math.Clamp(PreRollBox.SelectedIndex, 0, PreRolls.Length - 1)];
        a.Seek(Math.Max(FirstTrack.Start, mix - preRoll));
        a.Join(mix, b, fade, end);
        a.Play();
        _joinPlaying = true;
        _timer?.Start();
        PlayButton.Content = L.T("▶  Again");
    }

    void StopListening()
    {
        _firstPlayer?.Pause();
        _nextPlayer?.Pause();
        _firstPlayer?.ClearJoin();
        _joinPlaying = false;
        _timer?.Stop();
        Join.SetPosition(null);
        PlayButton.Content = L.T("▶  Play the join");
    }

    /// <summary>The playhead: in the first one until the next starts, then in the next; stops 10 s after the join.</summary>
    void Tick()
    {
        if (_firstPlayer is not { } a || _nextPlayer is not { } b) return;
        double at = b.IsPlaying ? b.Position - NextTrack.Start : a.Position - FirstJoin().Mix;
        Join.SetPosition(at);
        if (at > 10 || (!a.IsPlaying && !b.IsPlaying)) StopListening();
    }

    // ───────────────────────── Saving ─────────────────────────

    async Task Save()
    {
        StopListening();
        SaveButton.IsEnabled = false;
        try
        {
            if (_firstCues != _firstSaved) await _host.SaveCues(_first.Path, _firstCues);
            if (_nextCues != _nextSaved && _next.Path != _first.Path) await _host.SaveCues(_next.Path, _nextCues);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            SaveButton.IsEnabled = true;
        }
    }
}
