using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>A recorded voice track: its file, how far it starts over the song before, and when the next song comes in.</summary>
/// <param name="Overlap">Seconds it starts before the song before ends.</param>
/// <param name="NextAt">Seconds into it when the next song starts (null: when it ends).</param>
/// <param name="Length">Its length in seconds.</param>
public sealed record VoiceTake(string File, double Overlap, double? NextAt, double Length);

/// <summary>
/// Records a voice track between two items of a daily playlist (Sintoniza Pro): the end of the item before
/// plays on the Preview output, the host talks (over its ending if they like) and brings in the next item (under the
/// voice if they keep talking). The take can be played back as on air, its joins nudged, and recorded again.
/// </summary>
public partial class VoiceTrackWindow : Window
{
    static readonly double[] PreRolls = [5, 10, 15, 25];
    const double Nudge = 0.5;

    readonly SegueHost _host = null!;
    readonly SegueItem _before = null!, _after = null!;
    readonly string _folder = "";
    readonly DispatcherTimer? _timer;
    PreviewPlayer? _a, _b, _vt;
    VoiceRecorder? _recorder;
    string? _take; // the file being recorded or recorded
    double _aStart, _aEnd, _bStart, _overlap, _length;
    double? _nextAt;
    enum Stage { Idle, Listening, Talking, Done }
    Stage _stage;

    // Parameterless constructor for the XAML previewer.
    public VoiceTrackWindow() => InitializeComponent();

    /// <param name="folder">Where the take is recorded (a temporary name until it is saved).</param>
    /// <param name="input">The input chosen last time.</param>
    public VoiceTrackWindow(SegueHost host, SegueItem before, SegueItem after, string folder, string input)
    {
        InitializeComponent();
        Localization.Apply(this);
        _host = host;
        _before = before;
        _after = after;
        _folder = folder;
        BeforeTitle.Text = before.Title;
        AfterTitle.Text = after.Title;
        OnAirWarning.IsVisible = host.PreviewIsOnAir;

        var inputs = VoiceRecorder.Devices();
        InputBox.ItemsSource = inputs;
        InputBox.SelectedIndex = Math.Max(0, inputs.ToList().IndexOf(input));
        PreRollBox.ItemsSource = PreRolls.Select(s => L.F("{0:0} s before its end", s)).ToList();
        PreRollBox.SelectedIndex = 1;

        // Where the songs end and start, as the playout plays them.
        var cuesA = host.GetCues(before.Path);
        _a = host.Open(before.Path);
        _aStart = cuesA.CueIn ?? 0;
        _aEnd = Math.Min(cuesA.CueOut ?? double.MaxValue, _a?.Length ?? 0);
        _b = host.Open(after.Path);
        _bStart = host.GetCues(after.Path).CueIn ?? 0;
        if (_a == null || _b == null)
        {
            ErrorText.Text = L.F("{0} can't be opened.", _a == null ? before.Title : after.Title);
            StartButton.IsEnabled = false;
        }

        StartButton.Click += (_, _) => StartListening();
        TalkButton.Click += (_, _) => { if (_stage == Stage.Talking) StopTake(); else Talk(); };
        NextButton.Click += (_, _) => NextSong();
        StopButton.Click += (_, _) => { if (_stage == Stage.Talking) StopTake(); else StopAll(); };
        AgainButton.Click += (_, _) => { StopAll(); StartListening(); };
        PlayBackButton.Click += (_, _) => PlayBack();
        StopPlayButton.Click += (_, _) => StopAll();
        OverlapLess.Click += (_, _) => ChangeTake(() => _overlap = Math.Max(0, _overlap - Nudge));
        OverlapMore.Click += (_, _) => ChangeTake(() => _overlap = Math.Min(_aEnd - _aStart, _overlap + Nudge));
        NextLess.Click += (_, _) => ChangeTake(() => _nextAt = Math.Max(0, (_nextAt ?? _length) - Nudge));
        NextMore.Click += (_, _) => ChangeTake(() => _nextAt = (_nextAt ?? _length) + Nudge >= _length ? null : (_nextAt ?? _length) + Nudge);
        NextEndButton.Click += (_, _) => ChangeTake(() => _nextAt = null);
        CancelButton.Click += (_, _) => Close(null);
        SaveButton.Click += (_, _) =>
        {
            StopAll();
            if (_take != null) Close(new VoiceTake(_take, Math.Round(_overlap, 2), _nextAt is { } n ? Math.Round(n, 2) : null, _length));
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Space && _stage is Stage.Listening or Stage.Talking) { TalkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            else if (e.Key == Key.Enter && _stage == Stage.Talking) { NextSong(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            StopAll();
            _recorder?.Dispose();
            foreach (var p in new[] { _a, _b, _vt }) p?.Dispose();
            // A take that wasn't saved is thrown away.
            if (_take != null && !_saved) try { File.Delete(_take); } catch { /* left */ }
        };
        Show(Stage.Idle);
    }

    bool _saved;

    /// <summary>The take, when the window closes with Save (null: cancelled). The file is the caller's then.</summary>
    public new void Close(object? result)
    {
        _saved = result != null;
        base.Close(result);
    }

    public string Input => InputBox.SelectedItem as string ?? "";

    double PreRoll => PreRolls[Math.Clamp(PreRollBox.SelectedIndex, 0, PreRolls.Length - 1)];

    // ───────────────────────── Recording ─────────────────────────

    void StartListening()
    {
        if (_a is not { } a) return;
        StopAll();
        a.Seek(Math.Max(_aStart, _aEnd - PreRoll));
        a.Play();
        Show(Stage.Listening);
    }

    void Talk()
    {
        if (_a is not { } a) return;
        try
        {
            _recorder ??= new VoiceRecorder(Input);
            Directory.CreateDirectory(_folder);
            if (_take != null) try { File.Delete(_take); } catch { /* a new name below */ }
            _take = Path.Combine(_folder, $"take-{Guid.NewGuid():N}.mp3");
            _recorder.Start(_take);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }
        // How far before the end of the song it starts (0 once that song has ended).
        _overlap = a.IsPlaying ? Math.Max(0, _aEnd - a.Position) : 0;
        _nextAt = null;
        Show(Stage.Talking);
    }

    void NextSong()
    {
        if (_stage != Stage.Talking || _b is not { } b || _recorder is not { } r) return;
        _nextAt = r.Seconds;
        b.Seek(_bStart);
        b.Play();
        NextButton.IsEnabled = false;
    }

    void StopTake()
    {
        _recorder?.Stop();
        _a?.Pause();
        _b?.Pause();
        _length = _take != null && File.Exists(_take) ? AudioProbe.Duration(_take) ?? 0 : 0;
        if (_length < 0.5)
        {
            ErrorText.Text = L.T("Nothing was recorded: check the input.");
            Show(Stage.Idle);
            return;
        }
        if (_nextAt >= _length - 0.05) _nextAt = null;
        _vt?.Dispose();
        _vt = _host.Open(_take!);
        Show(Stage.Done);
    }

    void StopAll()
    {
        if (_stage == Stage.Talking) { StopTake(); return; }
        foreach (var p in new[] { _a, _b, _vt })
        {
            p?.Pause();
            p?.ClearJoin();
        }
        if (_stage == Stage.Listening) Show(Stage.Idle);
    }

    /// <summary>The end of the song before, the voice track and the start of the next, joined as the playout will.</summary>
    void PlayBack()
    {
        if (_a is not { } a || _b is not { } b || _vt is not { } vt) return;
        StopAll();
        double vtStart = _aEnd - _overlap;
        a.Seek(Math.Max(_aStart, vtStart - 4));
        vt.Seek(0);
        b.Seek(_bStart);
        a.Join(vtStart, vt, null, _aEnd);
        vt.Join(_nextAt ?? _length, b, null, _length);
        a.Play();
    }

    void ChangeTake(Action change)
    {
        change();
        Show(Stage.Done);
    }

    // ───────────────────────── Display ─────────────────────────

    void Show(Stage stage)
    {
        _stage = stage;
        StartButton.IsEnabled = stage is Stage.Idle or Stage.Done && _a != null;
        StartButton.Content = stage == Stage.Done ? L.T("▶  Start again") : L.T("▶  Start");
        TalkButton.IsEnabled = stage is Stage.Listening or Stage.Talking;
        TalkButton.Content = stage == Stage.Talking ? L.T("■  Stop talking") : L.T("●  Talk");
        NextButton.IsEnabled = stage == Stage.Talking && _nextAt == null;
        StopButton.IsEnabled = stage is Stage.Listening or Stage.Talking;
        InputBox.IsEnabled = PreRollBox.IsEnabled = stage is Stage.Idle or Stage.Done;
        TakePanel.IsVisible = stage == Stage.Done;
        SaveButton.IsEnabled = stage == Stage.Done;
        StatusText.Text = stage switch
        {
            Stage.Listening => L.F("{0} is playing: press Talk (Space) when you want to speak.", _before.Title),
            Stage.Talking => L.T("Recording. Next song (Enter) brings in the next one; Space stops."),
            Stage.Done => L.F("Take recorded: {0:0.0} s. Play it back, nudge the joins, then Save — or record again.", _length),
            _ => L.T("Press Start to hear the end of the song before."),
        };
        if (stage == Stage.Done)
        {
            OverlapText.Text = L.F("{0:0.0} s", _overlap);
            NextText.Text = _nextAt is { } n ? L.F("{0:0.0} s", n) : L.T("its end");
            NextEndButton.IsEnabled = _nextAt != null;
        }
    }

    void Tick()
    {
        LevelBar.Value = _recorder?.Level ?? 0;
        TimeText.Text = _stage switch
        {
            Stage.Talking when _recorder != null => Format.CueTime(_recorder.Seconds),
            Stage.Listening when _a != null => L.F("−{0:0.0} s to its end", Math.Max(0, _aEnd - _a.Position)),
            _ => "",
        };
    }
}
