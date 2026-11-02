using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Playout.Engine;

namespace Playout.App.Controls;

/// <summary>
/// A whole hour on one time line (the hour mix editor): its items on two lanes, one after the other, so where one
/// starts over the end of the one before shows. Each block has its waveform and title; the top of the hour is the red
/// line. Dragging a block that can move slides it (and everything after it); a click sets where listening starts; a
/// double-click on a block opens its join with the one before. The wheel scrolls, Ctrl + wheel zooms.
/// </summary>
public sealed class HourMixView : Control
{
    static readonly IBrush Back = new ImmutableSolidColorBrush(Color.Parse("#13161C"));
    static readonly IBrush LaneBack = new ImmutableSolidColorBrush(Color.Parse("#1A1D24"));
    static readonly IBrush Tick = new ImmutableSolidColorBrush(Color.Parse("#2A2F3A"));
    static readonly IBrush TickText = new ImmutableSolidColorBrush(Color.Parse("#7D8594"));
    static readonly IBrush TopOfHour = new ImmutableSolidColorBrush(Color.Parse("#E5484D"));
    static readonly IBrush Playhead = new ImmutableSolidColorBrush(Color.Parse("#FF5A5F"));
    static readonly IBrush CursorLine = new ImmutableSolidColorBrush(Color.Parse("#F5A524"));
    static readonly IBrush Text = new ImmutableSolidColorBrush(Color.Parse("#E7E9EE"));
    static readonly IBrush Overlap = new ImmutableSolidColorBrush(Color.Parse("#80F5A524"));
    static readonly IPen Selected = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#F5A524")), 2);
    static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    const double Ruler = 22;

    HourMix? _mix;

    /// <summary>Seconds per pixel… the other way round: how many pixels a second takes (Ctrl + wheel zooms).</summary>
    public double PixelsPerSecond { get; private set; } = 2;
    /// <summary>The second of the hour at the left edge.</summary>
    public double Offset { get; private set; }
    /// <summary>How long the line is: the hour and what runs over it.</summary>
    public double Total => Math.Max(3600, (_mix?.Blocks.Count > 0 ? _mix.Blocks.Max(b => b.End) : 0) + 30);
    /// <summary>How many seconds fit in the view now.</summary>
    public double Visible => Bounds.Width > 0 ? Bounds.Width / PixelsPerSecond : 0;

    public int SelectedIndex { get; private set; } = -1;
    /// <summary>Where listening starts (seconds into the hour).</summary>
    public double CursorAt { get; private set; }
    /// <summary>What is heard now (seconds into the hour); null: nothing.</summary>
    public double? PlayheadAt { get; private set; }

    /// <summary>A block is dragged: its index and by how many seconds (earlier: negative); returns how far it went.</summary>
    public Func<int, double, double>? Dragged { get; set; }
    public event Action<int>? SelectionChanged;
    /// <summary>A block was double-clicked: its join with the one before.</summary>
    public event Action<int>? JoinOpened;
    public event Action? ViewChanged;

    public void SetMix(HourMix mix)
    {
        _mix = mix;
        InvalidateVisual();
    }

    public void Select(int index)
    {
        SelectedIndex = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(index);
    }

    public void SetPlayhead(double? at)
    {
        PlayheadAt = at;
        // Follow what is heard, a page at a time.
        if (at is { } p && (p < Offset || p > Offset + Visible * 0.9)) ScrollTo(p - Visible * 0.1);
        InvalidateVisual();
    }

    public void ScrollTo(double seconds)
    {
        Offset = Math.Clamp(seconds, 0, Math.Max(0, Total - Visible));
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    /// <summary>Zoom around a second of the hour (by default the middle of the view).</summary>
    public void Zoom(double factor, double? around = null)
    {
        double at = around ?? Offset + Visible / 2;
        double x = (at - Offset) * PixelsPerSecond;
        double min = Bounds.Width > 0 ? Bounds.Width / Total : 0.1;
        PixelsPerSecond = Math.Clamp(PixelsPerSecond * factor, min, 200);
        ScrollTo(at - x / PixelsPerSecond);
    }

    /// <summary>The whole hour in the view.</summary>
    public void ZoomAll()
    {
        if (Bounds.Width <= 0) return;
        PixelsPerSecond = Bounds.Width / Total;
        ScrollTo(0);
    }

    double X(double seconds) => (seconds - Offset) * PixelsPerSecond;
    double Seconds(double x) => Offset + x / PixelsPerSecond;
    Rect LaneRect(int lane) { double h = (Bounds.Height - Ruler - 6) / 2; return new Rect(0, Ruler + 2 + lane * (h + 2), Bounds.Width, h); }
    Rect BlockRect(MixBlock b, int i) { var lane = LaneRect(i % 2); return new Rect(X(b.Start), lane.Y + 2, Math.Max(2, b.Length * PixelsPerSecond), lane.Height - 4); }

    static readonly Dictionary<TrackKind, IBrush> Kinds = Enum.GetValues<TrackKind>().ToDictionary(k => k, k => (IBrush)new ImmutableSolidColorBrush(Color.Parse(k switch
    {
        TrackKind.Jingle => "#6E52B0",
        TrackKind.Commercial => "#2E7D5B",
        TrackKind.Voice => "#B07A1E",
        TrackKind.Programme => "#2F5F9E",
        TrackKind.Stream => "#1F7F86",
        _ => "#2D5E96",
    })));
    static readonly IBrush WaveBrush = new ImmutableSolidColorBrush(Color.Parse("#B0E7E9EE"));

    static IBrush KindBrush(TrackKind kind) => Kinds[kind];

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        ctx.FillRectangle(Back, new Rect(b.Size));
        ctx.FillRectangle(LaneBack, LaneRect(0));
        ctx.FillRectangle(LaneBack, LaneRect(1));

        // The ruler: a mark every so often, by the zoom.
        double step = new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 }.FirstOrDefault(s => s * PixelsPerSecond >= 70, 600);
        for (double t = Math.Floor(Offset / step) * step; t <= Offset + Visible; t += step)
        {
            double x = X(t);
            ctx.FillRectangle(Tick, new Rect(Math.Round(x), Ruler - 6, 1, b.Height - Ruler + 6));
            var label = new FormattedText(Clock(t), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, TickText);
            ctx.DrawText(label, new Point(x + 3, 3));
        }

        if (_mix != null)
        {
            var blocks = _mix.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var r = BlockRect(block, i);
                if (r.Right < 0 || r.X > b.Width) continue;
                ctx.FillRectangle(KindBrush(block.Kind), r, 4);
                DrawWave(ctx, r, block);
                // Where it plays over the one before: shaded on the one before.
                if (i > 0 && _mix.Overlap(i) > 0.05)
                {
                    var before = BlockRect(blocks[i - 1], i - 1);
                    double x0 = Math.Max(before.X, r.X), x1 = before.Right;
                    if (x1 > x0) ctx.FillRectangle(Overlap, new Rect(x0, before.Y, x1 - x0, before.Height));
                }
                using (ctx.PushClip(r.Deflate(new Thickness(4, 0))))
                {
                    var title = new FormattedText((_mix.ByHand(i) ? "✋ " : "") + block.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Text);
                    ctx.DrawText(title, new Point(r.X + 5, r.Y + 3));
                    var at = new FormattedText(Clock(block.Start, true), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, TickText);
                    ctx.DrawText(at, new Point(r.X + 5, r.Bottom - at.Height - 3));
                }
                if (i == SelectedIndex) ctx.DrawRectangle(null, Selected, r, 4, 4);
            }
        }

        // The top of the hour.
        ctx.FillRectangle(TopOfHour, new Rect(Math.Round(X(3600)) - 1, 0, 2, b.Height));
        ctx.FillRectangle(CursorLine, new Rect(Math.Round(X(CursorAt)) - 1, 0, 2, b.Height));
        if (PlayheadAt is { } p) ctx.FillRectangle(Playhead, new Rect(Math.Round(X(p)) - 1, Ruler, 2, b.Height - Ruler));
    }

    static string Clock(double seconds, bool tenths = false)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        string text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        return tenths ? text + "." + (t.Milliseconds / 100) : text;
    }

    void DrawWave(DrawingContext ctx, Rect r, MixBlock block)
    {
        if (block.Wave is not { } wave || r.Width < 4) return;
        double mid = r.Y + r.Height * 0.6, amp = r.Height * 0.32, start = block.Cues.CueIn ?? 0;
        var brush = WaveBrush;
        double x0 = Math.Max(r.X, 0), x1 = Math.Min(r.Right, Bounds.Width);
        for (double x = x0; x < x1; x += 2)
        {
            double t0 = start + (x - r.X) / PixelsPerSecond, t1 = t0 + 2 / PixelsPerSecond;
            int i0 = Math.Clamp((int)(t0 * wave.PointsPerSecond), 0, wave.Peak.Length - 1);
            int i1 = Math.Clamp((int)Math.Ceiling(t1 * wave.PointsPerSecond), i0 + 1, wave.Peak.Length);
            float peak = 0;
            for (int i = i0; i < i1; i++) peak = Math.Max(peak, wave.Peak[i]);
            double h = Math.Max(0.5, peak * amp);
            ctx.FillRectangle(brush, new Rect(x, mid - h, 1.2, h * 2));
        }
    }

    // ───────────────────────── Mouse ─────────────────────────

    int _drag = -1;
    double _dragX;
    bool _dragged;

    int BlockAt(Point p)
    {
        if (_mix == null) return -1;
        for (int i = _mix.Blocks.Count - 1; i >= 0; i--)
            if (BlockRect(_mix.Blocks[i], i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        var p = point.Position;
        int i = p.Y < Ruler ? -1 : BlockAt(p);
        if (e.ClickCount == 2 && i > 0)
        {
            JoinOpened?.Invoke(i);
            e.Handled = true;
            return;
        }
        CursorAt = Math.Clamp(Seconds(p.X), 0, Total);
        if (i >= 0 && i != SelectedIndex) Select(i);
        _drag = i >= 0 && _mix!.Movable(i) ? i : -1;
        _dragX = p.X;
        _dragged = false;
        if (_drag >= 0) e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        double x = e.GetPosition(this).X;
        double seconds = Math.Round((x - _dragX) / PixelsPerSecond, 2);
        if (Math.Abs(seconds) < 0.01 || (!_dragged && Math.Abs(x - _dragX) < 3)) return;
        _dragged = true;
        // Only as far as it could go, so the block stays under the pointer at its limits.
        double moved = Dragged?.Invoke(_drag, seconds) ?? 0;
        _dragX += moved * PixelsPerSecond;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = -1;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            Zoom(e.Delta.Y > 0 ? 1.25 : 0.8, Seconds(e.GetPosition(this).X));
        else ScrollTo(Offset - (e.Delta.Y + e.Delta.X) * Visible * 0.1);
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width <= 0) ZoomAll();
        else ScrollTo(Offset);
    }
}
