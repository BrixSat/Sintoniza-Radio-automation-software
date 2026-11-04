using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Library;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Listeners: a day's listener counts hour by hour (the average, the most and the fewest) and, for an
/// hour, what played in it with the listeners when each item started.
/// </summary>
public partial class ListenersView : UserControl
{
    readonly LibraryDb? _db;
    List<ListenerSample> _samples = new();
    List<AsRunEntry> _played = new();
    int _hour = -1;
    int _loadVersion;

    // Parameterless constructor for the XAML previewer.
    public ListenersView() => InitializeComponent();

    public ListenersView(LibraryDb db)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        DayBox.SelectedDate = StationTime.Today;
        DayBox.SelectedDateChanged += async (_, _) => await Reload();
        BackButton.Click += (_, _) => DayBox.SelectedDate = Day.AddDays(-1);
        ForwardButton.Click += (_, _) => DayBox.SelectedDate = Day.AddDays(1);
        TodayButton.Click += (_, _) => DayBox.SelectedDate = StationTime.Today;
        ReloadButton.Click += async (_, _) => await Reload();
        AttachedToVisualTree += async (_, _) => await Reload();
    }

    DateTime Day => (DayBox.SelectedDate ?? StationTime.Today).Date;

    /// <summary>The day's counts and as-run log, read in the background.</summary>
    internal async Task Reload()
    {
        if (_db is not { } db) return;
        var day = Day;
        int version = ++_loadVersion;
        StatusText.Text = L.T("Reading…");
        try
        {
            var (samples, played, now) = await Task.Run(() => (ListenersDb.Read(db, day, day), AsRunDb.Read(db, day, day), ListenersDb.Now(db, StationTime.Now)));
            if (version != _loadVersion) return;
            _samples = samples;
            _played = played;
            Show(day, now);
        }
        catch (Exception ex)
        {
            if (version == _loadVersion) StatusText.Text = L.F("Can't read the listeners: {0}", ex.Message);
        }
    }

    void Show(DateTime day, ListenerSample? now)
    {
        var hours = ListenersDb.Hours(_samples).ToDictionary(h => h.Hour.Hour);
        var parts = new List<string>();
        if (day == StationTime.Today) parts.Add(now is { } n ? L.F("Now: {0} listening ({1:HH:mm})", n.Listeners, n.At) : L.T("Now: not counted"));
        if (_samples.Count > 0)
        {
            var peak = _samples.MaxBy(s => s.Listeners);
            parts.Add(L.F("most {0} at {1:HH:mm}", peak.Listeners, peak.At));
            parts.Add(L.F("on average {0:0.#}", _samples.Average(s => s.Listeners)));
        }
        else parts.Add(L.T("no listener counts this day"));
        StatusText.Text = string.Join(" · ", parts);

        // Scaled to the busiest hour's average (the bars are averages): one spike doesn't flatten the rest of the day.
        double top = hours.Count > 0 ? Math.Max(1, hours.Values.Max(h => h.Average)) : 1;
        ScaleText.Text = hours.Count > 0 ? L.F("Top of the chart: {0}", Math.Round(top, 1)) : "";
        Chart.Children.Clear();
        HourLabels.Children.Clear();
        Chart.ColumnDefinitions.Clear();
        HourLabels.ColumnDefinitions.Clear();
        for (int h = 0; h < 24; h++)
        {
            Chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            HourLabels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var cell = new Panel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            cell.Classes.Add("hour");
            if (hours.TryGetValue(h, out var hour))
            {
                var bar = new Border { Height = Math.Max(2, 150 * hour.Average / top) };
                bar.Classes.Add("bar");
                bar.Classes.Set("chosen", h == _hour);
                cell.Children.Add(bar);
                ToolTip.SetTip(cell, L.F("{0:00}:00–{0:00}:59: on average {1:0.#}, most {2}, fewest {3} ({4} counts)", h, hour.Average, hour.Peak, hour.Low, hour.Counts));
            }
            else ToolTip.SetTip(cell, L.F("{0:00}:00: not counted", h));
            int chosen = h;
            cell.PointerPressed += (_, _) => ShowHour(chosen);
            Grid.SetColumn(cell, h);
            Chart.Children.Add(cell);
            if (h % 3 == 0)
            {
                var label = new TextBlock { Text = $"{h:00}", FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#8B93A3")), HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(label, h);
                HourLabels.Children.Add(label);
            }
        }
        if (_hour < 0) _hour = day == StationTime.Today ? StationTime.Now.Hour : hours.Count > 0 ? hours.Values.MaxBy(x => x.Average)!.Hour.Hour : 8;
        ShowHour(_hour, redraw: false);
    }

    /// <summary>What played in an hour, each with the listeners when it started.</summary>
    internal void ShowHour(int hour, bool redraw = true)
    {
        _hour = hour;
        if (redraw)
            foreach (var cell in Chart.Children.OfType<Panel>())
                foreach (var bar in cell.Children.OfType<Border>()) bar.Classes.Set("chosen", Grid.GetColumn(cell) == hour);
        var items = _played.Where(e => e.Start.Hour == hour).ToList();
        HourTitle.Text = L.F("WHAT PLAYED {0:00}:00–{0:00}:59", hour).ToUpperInvariant();
        PlayedList.ItemsSource = items.Count == 0
            ? new[] { L.T("Nothing went on air in this hour (or it isn't in the as-run log yet).") }
            : items.Select(e =>
            {
                string who = ListenersDb.At(_samples, e.Start) is { } n ? n.ToString().PadLeft(6) : "     –";
                string what = e.Type == "Music" || e.Artist.Length > 0 ? $"{e.Artist} - {e.Title}".Trim(' ', '-') : e.Title.Length > 0 ? e.Title : Path.GetFileNameWithoutExtension(e.File);
                return $"{e.Start:HH:mm:ss}  {who}  {L.T(e.Type),-10}  {what}";
            }).ToArray();
    }
}
