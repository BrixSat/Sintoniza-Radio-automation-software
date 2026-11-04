using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// A studio clock face (Sintoniza Tempo): the seconds as a ring of 60 lights, lit round to the second now, the hours
/// as marks outside it, and the time in large figures in the middle (hours and minutes, the seconds under them). The
/// station's time (<see cref="StationTime"/>). Each figure keeps its own width, so nothing moves as they change.
/// </summary>
public sealed class StudioClock : Control
{
    static readonly IBrush Lit = new SolidColorBrush(Color.Parse("#E5484D"));
    static readonly IBrush Unlit = new SolidColorBrush(Color.Parse("#262A33"));
    static readonly IBrush Hour = new SolidColorBrush(Color.Parse("#78A2F5"));
    static readonly IBrush Figures = new SolidColorBrush(Color.Parse("#F2F4F8"));
    static readonly IBrush SecondsBrush = new SolidColorBrush(Color.Parse("#E5484D"));
    static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    readonly DispatcherTimer _timer;
    int _shown = -1;

    /// <summary>The second changed (and the face was redrawn): what goes with it is shown in step.</summary>
    public event Action? SecondChanged;

    /// <summary>The time drawn (for the checks: a time to show instead of now).</summary>
    public DateTime? Fixed { get; set; }

    public DateTime Time => Fixed ?? StationTime.Now;

    public StudioClock()
    {
        // A few times a second: the ring moves on as the second does, never a second late.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            int second = (int)(Time.Ticks / TimeSpan.TicksPerSecond % 86400);
            if (second == _shown) return;
            _shown = second;
            InvalidateVisual();
            SecondChanged?.Invoke();
        });
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        var now = Time;
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 10) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double radius = size / 2;

        // The seconds: 0 at the top, lit round to the one now; every fifth larger.
        int second = now.Second;
        for (int i = 0; i < 60; i++)
        {
            double angle = (i * 6 - 90) * Math.PI / 180;
            double r = radius * 0.80, dot = size * (i % 5 == 0 ? 0.019 : 0.012);
            var at = new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
            context.DrawEllipse(i <= second ? Lit : Unlit, null, at, dot, dot);
        }
        // The hours: a mark outside every fifth second.
        for (int i = 0; i < 12; i++)
        {
            double angle = (i * 30 - 90) * Math.PI / 180;
            double r = radius * 0.93, dot = size * 0.014;
            context.DrawEllipse(Hour, null, new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle)), dot, dot);
        }

        // The time: hours and minutes large, the seconds under them.
        Figures1(context, now.ToString("HH:mm", CultureInfo.InvariantCulture), size * 0.24, Figures, center.X, center.Y - size * 0.05);
        Figures1(context, now.ToString("ss", CultureInfo.InvariantCulture), size * 0.11, SecondsBrush, center.X, center.Y + size * 0.15);
    }

    /// <summary>Figures centred on <paramref name="x"/>, <paramref name="y"/>, each in a cell as wide as the widest digit.</summary>
    static void Figures1(DrawingContext context, string text, double fontSize, IBrush brush, double x, double y)
    {
        FormattedText Text(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, fontSize, brush);
        double digit = "0123456789".Max(c => Text(c.ToString()).Width);
        double colon = Text(":").Width;
        double width = text.Sum(c => c == ':' ? colon : digit);
        double left = x - width / 2;
        foreach (char c in text)
        {
            var t = Text(c.ToString());
            double cell = c == ':' ? colon : digit;
            context.DrawText(t, new Point(left + (cell - t.Width) / 2, y - t.Height / 2));
            left += cell;
        }
    }
}
