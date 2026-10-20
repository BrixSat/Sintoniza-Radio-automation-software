using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Playout.Engine;

namespace Playout.App.Controls;

public enum WaveformMode { Scrolling, Overview }

/// <summary>Marker positions to draw, in seconds from the start of the file. <see cref="Mix"/> is where the next item starts.</summary>
public readonly record struct CueMarkers(double CueIn, double? Intro, double? Outro, double Mix, double CueOut, bool MixIsMarker)
{
    public static CueMarkers For(Track t, double crossfade) =>
        new(t.Start, t.Cues?.Intro, t.Cues?.Outro, t.MixPoint(crossfade), t.End, t.Cues?.Mix != null);
}

/// <summary>
/// Waveform of the on-air track.
/// <list type="bullet">
/// <item><see cref="WaveformMode.Scrolling"/>: a zoomed window that scrolls past a fixed playhead, animated every frame.</item>
/// <item><see cref="WaveformMode.Overview"/>: the whole track, played part highlighted, crossfade zone marked.</item>
/// </list>
/// Cue points are drawn on both: the intro and mix zones shaded, the parts outside Cue In / Cue Out dimmed.
/// With <see cref="Interactive"/> set (the cue editor), clicking or dragging asks to seek and the wheel zooms.
/// The engine reports positions in steps; between reports the playhead is extrapolated from the wall clock
/// and gently pulled back toward the reported value, so motion stays smooth without drifting.
/// </summary>
public sealed class WaveformView : Control
{
    static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#13161C"));
    static readonly IBrush CenterLine = new ImmutableSolidColorBrush(Color.Parse("#262B35"));
    static readonly IBrush UpcomingPeak = new ImmutableSolidColorBrush(Color.Parse("#34507F"));
    static readonly IBrush UpcomingRms = new ImmutableSolidColorBrush(Color.Parse("#78A2F5"));
    static readonly IBrush PlayedPeak = new ImmutableSolidColorBrush(Color.Parse("#4A2428"));
    static readonly IBrush PlayedRms = new ImmutableSolidColorBrush(Color.Parse("#B9464B"));
    static readonly IBrush OverviewRest = new ImmutableSolidColorBrush(Color.Parse("#4A5263"));
    static readonly IBrush OverviewPlayed = new ImmutableSolidColorBrush(Color.Parse("#E5484D"));
    static readonly IBrush MixZone = new ImmutableSolidColorBrush(Color.Parse("#F5A524"), 0.16);
    static readonly IBrush MixLine = new ImmutableSolidColorBrush(Color.Parse("#F5A524"));
    static readonly IBrush Playhead = new ImmutableSolidColorBrush(Colors.White);
    static readonly IBrush IntroZone = new ImmutableSolidColorBrush(Color.Parse("#3ECF8E"), 0.14);
    static readonly IBrush IntroLine = new ImmutableSolidColorBrush(Color.Parse("#3ECF8E"));
    static readonly IBrush OutroLine = new ImmutableSolidColorBrush(Color.Parse("#B392F0"));
    static readonly IBrush CueLine = new ImmutableSolidColorBrush(Color.Parse("#E7E9EE"));
    static readonly IBrush Trimmed = new ImmutableSolidColorBrush(Color.Parse("#0B0D11"), 0.72);

    const double PlayheadFraction = 0.25;

    Waveform? _wave;
    double _duration;
    CueMarkers? _markers;

    // Smoothed playhead.
    double _anchorPos;
    long _anchorTicks = Stopwatch.GetTimestamp();
    bool _playing;
    bool _animating;

    // Overview column cache (rebuilt when the width or waveform changes).
    float[]? _columns;
    double _columnsWidth;

    public WaveformMode Mode { get; set; } = WaveformMode.Scrolling;

    /// <summary>Seconds visible in scrolling mode.</summary>
    public double WindowSeconds { get; set; } = 12;

    public void SetTrack(Waveform? wave, double duration)
    {
        _wave = wave;
        _duration = duration;
        _columns = null;
        InvalidateVisual();
    }

    /// <summary>Clicks and drags raise <see cref="SeekRequested"/>; the mouse wheel zooms the scrolling view.</summary>
    public bool Interactive { get; set; }

    /// <summary>The user clicked or dragged to this time (seconds). Only raised when <see cref="Interactive"/>.</summary>
    public event Action<double>? SeekRequested;

    public void SetMarkers(CueMarkers? markers)
    {
        if (_markers == markers) return;
        _markers = markers;
        InvalidateVisual();
    }

    public void UpdatePosition(double reported, bool playing)
    {
        double predicted = DisplayPosition();
        double error = reported - predicted;
        // Jumps (new track, seek, pause/resume) snap; small errors are blended out over a few updates.
        double next = !playing || !_playing || Math.Abs(error) > 0.25 ? reported : predicted + error * 0.15;

        _anchorPos = next;
        _anchorTicks = Stopwatch.GetTimestamp();
        _playing = playing;

        if (playing) StartAnimating();
        else InvalidateVisual();
    }

    double DisplayPosition() =>
        _playing ? _anchorPos + Stopwatch.GetElapsedTime(_anchorTicks).TotalSeconds : _anchorPos;

    void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    void OnFrame(TimeSpan _)
    {
        InvalidateVisual();
        if (_playing && TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(OnFrame);
        else _animating = false;
    }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Background, b, 4);
        double mid = b.Height / 2;

        if (_wave == null || _duration <= 0)
        {
            ctx.FillRectangle(CenterLine, new Rect(0, mid - 0.5, b.Width, 1));
            return;
        }

        using (ctx.PushClip(new RoundedRect(b, 4)))
        {
            if (Mode == WaveformMode.Scrolling) RenderScrolling(ctx, b, mid);
            else RenderOverview(ctx, b, mid);
        }
    }

    void RenderScrolling(DrawingContext ctx, Rect b, double mid)
    {
        var wave = _wave!;
        double pos = Math.Clamp(DisplayPosition(), 0, _duration);
        double pxPerSec = b.Width / WindowSeconds;
        double headX = Math.Round(b.Width * PlayheadFraction);
        const double step = 3, bar = 2;
        double amp = mid - 3;

        ctx.FillRectangle(CenterLine, new Rect(0, mid - 0.5, b.Width, 1));

        double X(double t) => headX + (t - pos) * pxPerSec;
        DrawZones(ctx, b, X);

        // Align bars to absolute time so they don't shimmer as the view moves.
        double secPerStep = step / pxPerSec;
        double firstT = Math.Floor((pos - headX / pxPerSec) / secPerStep) * secPerStep;
        for (double t = firstT; ; t += secPerStep)
        {
            double x = headX + (t - pos) * pxPerSec;
            if (x > b.Width) break;
            if (t < 0 || t >= _duration) continue;

            var (peak, rms) = Range(wave, t, t + secPerStep);
            bool played = t + secPerStep <= pos;
            double hp = Math.Max(1, peak * amp), hr = Math.Max(0.5, rms * amp);
            ctx.FillRectangle(played ? PlayedPeak : UpcomingPeak, new Rect(x, mid - hp, bar, hp * 2));
            ctx.FillRectangle(played ? PlayedRms : UpcomingRms, new Rect(x, mid - hr, bar, hr * 2));
        }

        // End of track.
        double endX = headX + (_duration - pos) * pxPerSec;
        if (endX < b.Width) ctx.FillRectangle(OverviewPlayed, new Rect(endX, 0, 2, b.Height));

        DrawLines(ctx, b, X, labels: true);
        ctx.FillRectangle(Playhead, new Rect(headX - 1, 0, 2, b.Height));
    }

    void RenderOverview(DrawingContext ctx, Rect b, double mid)
    {
        const double step = 2, bar = 1.5;
        int cols = Math.Max(1, (int)(b.Width / step));
        if (_columns == null || Math.Abs(_columnsWidth - b.Width) > 0.5)
        {
            _columns = new float[cols];
            for (int c = 0; c < cols; c++)
                _columns[c] = Range(_wave!, _duration * c / cols, _duration * (c + 1) / cols).Peak;
            _columnsWidth = b.Width;
        }

        double pos = Math.Clamp(DisplayPosition(), 0, _duration);
        double playX = pos / _duration * b.Width;

        double X(double t) => t / _duration * b.Width;
        DrawZones(ctx, b, X);

        double amp = mid - 1;
        for (int c = 0; c < _columns.Length; c++)
        {
            double x = c * step;
            double h = Math.Max(0.5, _columns[c] * amp);
            ctx.FillRectangle(x + bar <= playX ? OverviewPlayed : OverviewRest, new Rect(x, mid - h, bar, h * 2));
        }

        DrawLines(ctx, b, X, labels: b.Height >= 40);
        ctx.FillRectangle(Playhead, new Rect(Math.Min(b.Width - 2, playX), 0, 2, b.Height));
    }

    /// <summary>Shaded areas behind the waveform: the intro (Cue In → Intro) and the mix zone (Mix → Cue Out).</summary>
    void DrawZones(DrawingContext ctx, Rect b, Func<double, double> x)
    {
        if (_markers is not { } m) return;
        if (m.Intro is { } intro && intro > m.CueIn) Band(ctx, b, x(m.CueIn), x(Math.Min(intro, m.CueOut)), IntroZone);
        if (m.Mix < m.CueOut) Band(ctx, b, x(m.Mix), x(m.CueOut), MixZone);
    }

    /// <summary>Over the waveform: dimmed parts outside Cue In / Cue Out, then the marker lines and their labels.</summary>
    void DrawLines(DrawingContext ctx, Rect b, Func<double, double> x, bool labels)
    {
        if (_markers is not { } m) return;
        if (m.CueIn > 0) Band(ctx, b, x(0), x(m.CueIn), Trimmed);
        if (m.CueOut < _duration) Band(ctx, b, x(m.CueOut), x(_duration), Trimmed);

        // Cue In/Out labels sit at the bottom so they don't collide with Intro/Mix at the same spot.
        if (m.CueIn > 0) Line(ctx, b, x(m.CueIn), CueLine, labels ? "IN" : null, bottom: true);
        if (m.Intro is { } intro) Line(ctx, b, x(intro), IntroLine, labels ? "INTRO" : null);
        if (m.Outro is { } outro) Line(ctx, b, x(outro), OutroLine, labels ? "OUTRO" : null);
        if (m.Mix < m.CueOut || m.MixIsMarker) Line(ctx, b, x(m.Mix), MixLine, labels ? "MIX" : null);
        if (m.CueOut < _duration) Line(ctx, b, x(m.CueOut), CueLine, labels ? "OUT" : null, bottom: true, leftOfLine: true);
    }

    static void Band(DrawingContext ctx, Rect b, double x0, double x1, IBrush brush)
    {
        x0 = Math.Max(0, x0);
        x1 = Math.Min(b.Width, x1);
        if (x1 > x0) ctx.FillRectangle(brush, new Rect(x0, 0, x1 - x0, b.Height));
    }

    static void Line(DrawingContext ctx, Rect b, double x, IBrush brush, string? label, bool bottom = false, bool leftOfLine = false)
    {
        if (x < -40 || x > b.Width + 40) return;
        ctx.FillRectangle(brush, new Rect(Math.Round(x), 0, 1, b.Height));
        if (label == null) return;
        var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFace, 9, brush);
        double lx = leftOfLine ? x - ft.Width - 4 : x + 4;
        ctx.DrawText(ft, new Point(lx, bottom ? b.Height - ft.Height - 2 : 2));
    }

    // ───────────────────────── Interaction (cue editor) ─────────────────────────

    bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive || _duration <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        SeekRequested?.Invoke(TimeAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // In the scrolling view the waveform moves under the pointer, so only the overview scrubs.
        if (_dragging && Mode == WaveformMode.Overview) SeekRequested?.Invoke(TimeAt(e.GetPosition(this).X));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!Interactive || Mode != WaveformMode.Scrolling) return;
        WindowSeconds = Math.Clamp(WindowSeconds * (e.Delta.Y > 0 ? 0.8 : 1.25), 2, 120);
        InvalidateVisual();
        e.Handled = true;
    }

    double TimeAt(double x)
    {
        double w = Math.Max(1, Bounds.Width);
        double t = Mode == WaveformMode.Overview
            ? x / w * _duration
            : DisplayPosition() + (x - Math.Round(w * PlayheadFraction)) / (w / WindowSeconds);
        return Math.Clamp(t, 0, _duration);
    }

    static (float Peak, float Rms) Range(Waveform w, double t0, double t1)
    {
        int i0 = Math.Clamp((int)(t0 * w.PointsPerSecond), 0, w.Peak.Length - 1);
        int i1 = Math.Clamp((int)Math.Ceiling(t1 * w.PointsPerSecond), i0 + 1, w.Peak.Length);
        float peak = 0, rms = 0;
        for (int i = i0; i < i1; i++)
        {
            if (w.Peak[i] > peak) peak = w.Peak[i];
            if (w.Rms[i] > rms) rms = w.Rms[i];
        }
        return (peak, rms);
    }

    static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
}
