using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Playout.Engine;

namespace Playout.App.Controls;

public enum WaveformMode { Scrolling, Overview }

/// <summary>
/// Waveform of the on-air track.
/// <list type="bullet">
/// <item><see cref="WaveformMode.Scrolling"/>: a zoomed window that scrolls past a fixed playhead, animated every frame.</item>
/// <item><see cref="WaveformMode.Overview"/>: the whole track, played part highlighted, crossfade zone marked.</item>
/// </list>
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
    static readonly IBrush LabelBrush = new ImmutableSolidColorBrush(Color.Parse("#F5A524"));

    const double PlayheadFraction = 0.25;

    Waveform? _wave;
    double _duration;
    double _crossfade;

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

    public void SetCrossfade(double seconds)
    {
        if (Math.Abs(seconds - _crossfade) < 0.001) return;
        _crossfade = seconds;
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

        // Mix (crossfade) zone, shaded from the crossfade point to the end.
        double mixAt = _duration - _crossfade;
        if (_crossfade > 0)
        {
            double x0 = headX + (mixAt - pos) * pxPerSec;
            double x1 = headX + (_duration - pos) * pxPerSec;
            if (x1 > 0 && x0 < b.Width)
            {
                ctx.FillRectangle(MixZone, new Rect(Math.Max(0, x0), 0, Math.Min(b.Width, x1) - Math.Max(0, x0), b.Height));
                if (x0 >= 0 && x0 < b.Width)
                {
                    ctx.FillRectangle(MixLine, new Rect(x0, 0, 1, b.Height));
                    DrawLabel(ctx, "MIX", new Point(x0 + 4, 2));
                }
            }
        }

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

        if (_crossfade > 0 && _crossfade < _duration)
        {
            double x0 = (_duration - _crossfade) / _duration * b.Width;
            ctx.FillRectangle(MixZone, new Rect(x0, 0, b.Width - x0, b.Height));
            ctx.FillRectangle(MixLine, new Rect(x0, 0, 1, b.Height));
        }

        double amp = mid - 1;
        for (int c = 0; c < _columns.Length; c++)
        {
            double x = c * step;
            double h = Math.Max(0.5, _columns[c] * amp);
            ctx.FillRectangle(x + bar <= playX ? OverviewPlayed : OverviewRest, new Rect(x, mid - h, bar, h * 2));
        }

        ctx.FillRectangle(Playhead, new Rect(Math.Min(b.Width - 2, playX), 0, 2, b.Height));
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

    static void DrawLabel(DrawingContext ctx, string text, Point at)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFace, 9, LabelBrush);
        ctx.DrawText(ft, at);
    }
}
