using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Playout.App.Controls;

/// <summary>What a calendar cell shows: its text, whether the selected item is there, and whether it can be used.</summary>
/// <param name="Other">Something else is there (shown in a softer colour).</param>
public readonly record struct GridCell(string Text, bool On, bool Enabled = true, bool Other = false);

/// <summary>
/// A week calendar: times of day down the side, a column per day. A click puts the selected item in a cell or
/// takes it out; dragging does the same to every cell of the rectangle; a click on a time does the whole row.
/// Drawn by itself, so a week of 10-minute rows stays quick.
/// </summary>
public sealed class WeekGrid : Control
{
    public const double TimeWidth = 74;
    public const double RowHeight = 22;

    static readonly IBrush Stripe = new SolidColorBrush(Color.Parse("#171A21"));
    static readonly IBrush Line = new SolidColorBrush(Color.Parse("#232833"));
    static readonly IBrush HourLine = new SolidColorBrush(Color.Parse("#3A4150"));
    static readonly IBrush OnFill = new SolidColorBrush(Color.Parse("#2B4C8C"));
    static readonly IBrush OnBorder = new SolidColorBrush(Color.Parse("#4C8DFF"));
    static readonly IBrush Disabled = new SolidColorBrush(Color.Parse("#0E1014"));
    static readonly IBrush Hover = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    static readonly IBrush TextOn = Brushes.White;
    static readonly IBrush TextOther = new SolidColorBrush(Color.Parse("#AEB6C4"));
    static readonly IBrush TextTime = new SolidColorBrush(Color.Parse("#8B93A3"));
    static readonly Typeface Face = new(FontFamily.Default);

    IReadOnlyList<double> _times = [];
    (int Col, int Row)? _anchor, _hover;
    bool _paintValue;

    /// <summary>The rows: times of day in seconds.</summary>
    public IReadOnlyList<double> Times
    {
        get => _times;
        set
        {
            _times = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public int Columns { get; set; } = 7;

    /// <summary>What each cell (column, row) shows.</summary>
    public Func<int, int, GridCell>? CellOf { get; set; }

    /// <summary>A paint starts (remember the state, to put back while the rectangle changes).</summary>
    public event Action? PaintStarted;

    /// <summary>The cells of the rectangle get the item (true) or lose it; the window puts back the state from
    /// <see cref="PaintStarted"/> first, so a rectangle that shrinks leaves the cells it no longer covers as they were.</summary>
    public event Action<IReadOnlyList<(int Col, int Row)>, bool>? Paint;

    public event Action? PaintEnded;

    public WeekGrid()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void Refresh() => InvalidateVisual();

    ScrollViewer? _scroll;

    // Only the rows in view are drawn: draw again when they scroll.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll != null) _scroll.ScrollChanged += Scrolled;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroll != null) _scroll.ScrollChanged -= Scrolled;
        _scroll = null;
    }

    void Scrolled(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 600 : available.Width, Math.Max(RowHeight, _times.Count * RowHeight));

    double ColumnWidth => Math.Max(20, (Bounds.Width - TimeWidth) / Math.Max(1, Columns));

    /// <summary>The cell under a point; column -1 is the time column.</summary>
    (int Col, int Row)? CellAt(Point p)
    {
        int row = (int)(p.Y / RowHeight);
        if (row < 0 || row >= _times.Count) return null;
        if (p.X < TimeWidth) return (-1, row);
        int col = (int)((p.X - TimeWidth) / ColumnWidth);
        return col >= 0 && col < Columns ? (col, row) : null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || CellAt(e.GetPosition(this)) is not { } cell || CellOf == null) return;
        // A time: the whole row; its value from the first day that can take it.
        int first = cell.Col >= 0 ? cell.Col : Enumerable.Range(0, Columns).FirstOrDefault(c => CellOf(c, cell.Row).Enabled, -1);
        if (first < 0 || !CellOf(first, cell.Row).Enabled) return;
        _paintValue = cell.Col >= 0 ? !CellOf(first, cell.Row).On
            : !Enumerable.Range(0, Columns).Where(c => CellOf(c, cell.Row).Enabled).All(c => CellOf(c, cell.Row).On);
        _anchor = cell;
        e.Pointer.Capture(this);
        PaintStarted?.Invoke();
        PaintTo(cell);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var cell = CellAt(e.GetPosition(this));
        if (cell != _hover)
        {
            _hover = cell;
            InvalidateVisual();
        }
        if (_anchor != null && cell is { } c) PaintTo(c);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_anchor == null) return;
        _anchor = null;
        e.Pointer.Capture(null);
        PaintEnded?.Invoke();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    void PaintTo((int Col, int Row) to)
    {
        if (_anchor is not { } a || CellOf == null) return;
        bool wholeRows = a.Col < 0;
        int c0 = wholeRows ? 0 : Math.Min(a.Col, Math.Max(0, to.Col)), c1 = wholeRows ? Columns - 1 : Math.Max(a.Col, Math.Max(0, to.Col));
        int r0 = Math.Min(a.Row, to.Row), r1 = Math.Max(a.Row, to.Row);
        var cells = new List<(int, int)>();
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                if (CellOf(c, r).Enabled) cells.Add((c, r));
        Paint?.Invoke(cells, _paintValue);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        // Clicks only reach what is drawn: the whole area, so empty cells take them too.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double w = ColumnWidth;
        // Only the rows in view.
        int firstRow = 0, lastRow = _times.Count - 1;
        if (_scroll is { } sv)
        {
            firstRow = Math.Max(0, (int)(sv.Offset.Y / RowHeight) - 1);
            lastRow = Math.Min(_times.Count - 1, (int)((sv.Offset.Y + sv.Viewport.Height) / RowHeight) + 1);
        }
        for (int r = firstRow; r <= lastRow; r++)
        {
            double y = r * RowHeight;
            double t = _times[r];
            bool newHour = r == 0 || (int)(_times[r - 1] / 3600) != (int)(t / 3600);
            if ((int)(t / 3600) % 2 == 1) ctx.FillRectangle(Stripe, new Rect(0, y, Bounds.Width, RowHeight));
            ctx.FillRectangle(newHour ? HourLine : Line, new Rect(0, y, Bounds.Width, 1));
            Text(ctx, Time(t), new Point(8, y + 4), newHour ? TextOther : TextTime, 12, TimeWidth - 10, bold: newHour);

            for (int c = 0; c < Columns; c++)
            {
                var cell = CellOf?.Invoke(c, r) ?? default;
                var box = new Rect(TimeWidth + c * w + 1, y + 2, w - 2, RowHeight - 3);
                if (!cell.Enabled) ctx.FillRectangle(Disabled, box);
                else if (cell.On)
                {
                    ctx.FillRectangle(OnFill, box, 3);
                    ctx.DrawRectangle(new Pen(OnBorder, 1), box.Deflate(0.5), 3);
                }
                if (_hover is { } h && h.Row == r && (h.Col == c || h.Col < 0) && cell.Enabled) ctx.FillRectangle(Hover, box, 3);
                if (cell.Text.Length > 0)
                    Text(ctx, cell.Text, new Point(box.X + 5, y + 4), cell.On ? TextOn : TextOther, 11.5, box.Width - 8, bold: cell.On);
            }
        }
        for (int c = 0; c <= Columns; c++)
            ctx.FillRectangle(Line, new Rect(TimeWidth + c * w - 0.5, 0, 1, _times.Count * RowHeight));
    }

    static void Text(DrawingContext ctx, string text, Point at, IBrush brush, double size, double maxWidth, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold) : Face, size, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        ctx.DrawText(ft, at);
    }

    /// <summary>"07:20", or "07:20:15" when it has seconds.</summary>
    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.Seconds == 0 ? $"{(int)t.TotalHours:00}:{t.Minutes:00}" : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
