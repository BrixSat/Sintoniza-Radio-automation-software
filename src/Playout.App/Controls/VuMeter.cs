using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Playout.App.Controls;

/// <summary>
/// Horizontal segmented peak meter on a dB scale (-60..0 dBFS) with falling ballistics and peak hold.
/// Feed it linear levels (0..1) through <see cref="Push"/>; it smooths and redraws itself.
/// </summary>
public sealed class VuMeter : Control
{
    const double MinDb = -60;
    const int Segments = 40;
    const double FallPerSecond = 26;  // dB/s
    const double PeakHoldSeconds = 1.5;

    static readonly IBrush Off = new SolidColorBrush(Color.Parse("#262A33"));
    static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3ECF8E"));
    static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F5C524"));
    static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D"));

    double _db = MinDb;
    double _peakDb = MinDb;
    DateTime _peakAt;
    DateTime _last = DateTime.UtcNow;

    public void Push(float linear)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _last).TotalSeconds;
        _last = now;

        double db = linear > 0 ? Math.Max(MinDb, 20 * Math.Log10(linear)) : MinDb;
        _db = db >= _db ? db : Math.Max(db, _db - FallPerSecond * dt);

        if (db >= _peakDb || (now - _peakAt).TotalSeconds > PeakHoldSeconds)
        {
            _peakDb = db;
            _peakAt = now;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        const double gap = 2;
        double w = (b.Width - gap * (Segments - 1)) / Segments;
        if (w <= 0) return;

        int lit = (int)Math.Round((_db - MinDb) / -MinDb * Segments);
        int peak = (int)Math.Round((_peakDb - MinDb) / -MinDb * Segments) - 1;

        for (int i = 0; i < Segments; i++)
        {
            double segDb = MinDb + (i + 1) * (-MinDb / Segments);
            IBrush on = segDb > -3 ? Red : segDb > -12 ? Yellow : Green;
            var brush = i < lit || i == peak && peak > 0 ? on : Off;
            ctx.FillRectangle(brush, new Rect(i * (w + gap), 0, w, b.Height), 1.5f);
        }
    }
}
