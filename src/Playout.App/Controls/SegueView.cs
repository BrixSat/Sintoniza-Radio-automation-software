using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Playout.Engine;

namespace Playout.App.Controls;

/// <summary>
/// The join of two items on one time line (the segue editor): the end of the first (top) and the start of the next
/// (bottom), lined up so the next starts at 0 — the first one's Mix point. Seconds before the join are negative.
/// Dragging a lane slides that item: the first one's Mix point or the next one's Cue In moves. The first one's fade
/// (or its playing on to Cue Out) and the next one's Intro are drawn; the mouse wheel zooms.
/// </summary>
public sealed class SegueView : Control
{
    static readonly IBrush Back = new ImmutableSolidColorBrush(Color.Parse("#13161C"));
    static readonly IBrush LaneBack = new ImmutableSolidColorBrush(Color.Parse("#1A1D24"));
    static readonly IBrush Tick = new ImmutableSolidColorBrush(Color.Parse("#2A2F3A"));
    static readonly IBrush TickText = new ImmutableSolidColorBrush(Color.Parse("#7D8594"));
    static readonly IBrush FirstWave = new ImmutableSolidColorBrush(Color.Parse("#3E8EDE"));
    static readonly IBrush NextWave = new ImmutableSolidColorBrush(Color.Parse("#2FA37A"));
    static readonly IBrush Silent = new ImmutableSolidColorBrush(Color.Parse("#B013161C"));
    static readonly IBrush JoinLine = new ImmutableSolidColorBrush(Color.Parse("#F5A524"));
    static readonly IBrush OutLine = new ImmutableSolidColorBrush(Color.Parse("#E7E9EE"));
    static readonly IBrush IntroLine = new ImmutableSolidColorBrush(Color.Parse("#3ECF8E"));
    static readonly IBrush Playhead = new ImmutableSolidColorBrush(Color.Parse("#FF5A5F"));
    static readonly IPen FadePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FFD48A")), 1.5);
    static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    Waveform? _first, _next;
    double _firstLength, _nextLength;

    /// <summary>Where the next item starts, in the first one's file (seconds).</summary>
    public double Mix { get; private set; }
    /// <summary>Where the first one stops (its Cue Out).</summary>
    public double FirstEnd { get; private set; }
    /// <summary>How long the first one fades out from the join; null = it plays on unfaded to its Cue Out.</summary>
    public double? Fade { get; private set; }
    /// <summary>Where the next one starts in its file (Cue In), and its Intro (null: not set).</summary>
    public double NextStart { get; private set; }
    public double? NextIntro { get; private set; }

    /// <summary>How many seconds the view shows (the wheel zooms).</summary>
    public double Seconds { get; set; } = 24;

    /// <summary>Where the sound being heard is, from the join (null: nothing playing).</summary>
    public double? Position { get; private set; }

    /// <summary>A lane was dragged: the first one's Mix point (true) or the next one's Cue In (false) moves by this.</summary>
    public event Action<bool, double>? Slid;

    public SegueView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public void SetWaves(Waveform? first, double firstLength, Waveform? next, double nextLength)
    {
        (_first, _firstLength, _next, _nextLength) = (first, firstLength, next, nextLength);
        InvalidateVisual();
    }

    public void SetJoin(double mix, double firstEnd, double? fade, double nextStart, double? nextIntro)
    {
        (Mix, FirstEnd, Fade, NextStart, NextIntro) = (mix, firstEnd, fade, nextStart, nextIntro);
        InvalidateVisual();
    }

    public void SetPosition(double? position)
    {
        if (Position == position) return;
        Position = position;
        InvalidateVisual();
    }

    const double Ruler = 22;

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        ctx.FillRectangle(Back, b, 6);
        double laneH = (b.Height - Ruler - 12) / 2;
        var top = new Rect(0, Ruler, b.Width, laneH);
        var bottom = new Rect(0, Ruler + laneH + 12, b.Width, laneH);
        double pxPerSec = b.Width / Seconds;
        double X(double t) => b.Width / 2 + t * pxPerSec;

        // The ruler: a tick every second, a label every 2 or 5.
        int every = Seconds > 40 ? 10 : Seconds > 16 ? 2 : 1;
        for (int s = (int)Math.Floor(-Seconds / 2); s <= Seconds / 2; s++)
        {
            double x = X(s);
            ctx.FillRectangle(Tick, new Rect(x, Ruler - (s % every == 0 ? 8 : 4), 1, s % every == 0 ? 8 : 4));
            if (s % every != 0) continue;
            var label = new FormattedText(s == 0 ? "0" : s.ToString("+0;−0", CultureInfo.InvariantCulture) + " s", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Face, 10, TickText);
            if (x - label.Width / 2 >= 0 && x + label.Width / 2 <= b.Width) ctx.DrawText(label, new Point(x - label.Width / 2, 1));
        }

        ctx.FillRectangle(LaneBack, top, 4);
        ctx.FillRectangle(LaneBack, bottom, 4);
        // The first one: its file at Mix + t; silent past its Cue Out.
        Lane(ctx, top, _first, _firstLength, t => Mix + t, FirstWave, pxPerSec);
        double outX = X(FirstEnd - Mix);
        if (outX < b.Width) ctx.FillRectangle(Silent, new Rect(Math.Max(0, outX), top.Y, b.Width - Math.Max(0, outX), top.Height));
        if (outX >= 0 && outX <= b.Width) Marker(ctx, top, outX, OutLine, "OUT");
        // Its volume: full, then the fade from the join (or full to Cue Out).
        double fadeEnd = Fade is { } f ? Math.Min(FirstEnd - Mix, f) : FirstEnd - Mix;
        var envelope = new StreamGeometry();
        using (var g = envelope.Open())
        {
            g.BeginFigure(new Point(0, top.Y + 3), false);
            g.LineTo(new Point(X(0), top.Y + 3));
            if (Fade != null) g.LineTo(new Point(X(fadeEnd), top.Bottom - 3));
            else
            {
                g.LineTo(new Point(X(fadeEnd), top.Y + 3));
                g.LineTo(new Point(X(fadeEnd), top.Bottom - 3));
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, FadePen, envelope);

        // The next one: its file at Cue In + t; nothing before the join.
        Lane(ctx, bottom, _next, _nextLength, t => NextStart + t, NextWave, pxPerSec);
        ctx.FillRectangle(Silent, new Rect(0, bottom.Y, X(0), bottom.Height));
        if (NextIntro is { } intro && intro > NextStart) Marker(ctx, bottom, X(intro - NextStart), IntroLine, "INTRO");

        // The join, across both lanes.
        ctx.FillRectangle(JoinLine, new Rect(Math.Round(X(0)) - 1, Ruler, 2, b.Height - Ruler));
        if (Position is { } p && X(p) is var px && px >= 0 && px <= b.Width)
            ctx.FillRectangle(Playhead, new Rect(Math.Round(px) - 1, Ruler, 2, b.Height - Ruler));
    }

    static void Lane(DrawingContext ctx, Rect lane, Waveform? wave, double length, Func<double, double> fileTime, IBrush brush, double pxPerSec)
    {
        double mid = lane.Y + lane.Height / 2, amp = lane.Height / 2 - 4;
        if (wave == null || length <= 0)
        {
            ctx.FillRectangle(brush, new Rect(0, mid - 0.5, lane.Width, 1));
            return;
        }
        const double step = 3, bar = 2;
        for (double x = 0; x < lane.Width; x += step)
        {
            double t0 = fileTime((x - lane.Width / 2) / pxPerSec), t1 = t0 + step / pxPerSec;
            if (t1 <= 0 || t0 >= length) continue;
            int i0 = Math.Clamp((int)(t0 * wave.PointsPerSecond), 0, wave.Peak.Length - 1);
            int i1 = Math.Clamp((int)Math.Ceiling(t1 * wave.PointsPerSecond), i0 + 1, wave.Peak.Length);
            float peak = 0;
            for (int i = i0; i < i1; i++) peak = Math.Max(peak, wave.Peak[i]);
            double h = Math.Max(0.5, peak * amp);
            ctx.FillRectangle(brush, new Rect(x, mid - h, bar, h * 2));
        }
    }

    static void Marker(DrawingContext ctx, Rect lane, double x, IBrush brush, string label)
    {
        ctx.FillRectangle(brush, new Rect(Math.Round(x), lane.Y, 1, lane.Height));
        var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, brush);
        ctx.DrawText(text, new Point(x + 4, lane.Bottom - text.Height - 2));
    }

    // ───────────────────────── Sliding ─────────────────────────

    bool? _dragFirst;
    double _dragX;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (p.Y < Ruler) return;
        _dragFirst = p.Y < Ruler + (Bounds.Height - Ruler) / 2;
        _dragX = p.X;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFirst is not { } first) return;
        double x = e.GetPosition(this).X;
        // Steps of a hundredth of a second; the item moves with the pointer, so its marker moves the other way.
        double seconds = Math.Round((x - _dragX) / (Bounds.Width / Seconds), 2);
        if (seconds == 0) return;
        _dragX += seconds * (Bounds.Width / Seconds);
        Slid?.Invoke(first, -seconds);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFirst = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Seconds = Math.Clamp(Seconds * (e.Delta.Y > 0 ? 0.8 : 1.25), 4, 90);
        InvalidateVisual();
        e.Handled = true;
    }
}
