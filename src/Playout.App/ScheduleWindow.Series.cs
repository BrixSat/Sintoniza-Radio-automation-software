using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace Playout.App;

/// <summary>Sintoniza Pro → Events: an event's series, a file for each weekday and for given dates.</summary>
public partial class ScheduleWindow
{
    ScheduledEvent? SelectedEvent => (EventList.SelectedItem as ScheduleRow)?.Item as ScheduledEvent;

    void WireSeries()
    {
        EventSeriesDateBox.SelectedDate = DateTime.Today;
        EventSeriesDateButton.Click += (_, _) =>
        {
            if (SelectedEvent is not { } e || EventSeriesDateBox.SelectedDate is not { } picked) return;
            var date = picked.Date;
            if (e.Series.All(f => f.Date != date))
                Edit<ScheduledEvent>(EventList, ev => ev.Series.Add(new SeriesFile { Date = date }));
            ShowSeriesDates(e, focus: date);
        };
        EventSeriesPastButton.Click += (_, _) =>
        {
            if (SelectedEvent is not { } e) return;
            Edit<ScheduledEvent>(EventList, ev => ev.Series.RemoveAll(f => f.Date < DateTime.Today));
            ShowSeriesDates(e);
        };
    }

    /// <summary>The weekdays (from the first day of the week here) and the dates of the selected event's series.</summary>
    void ShowSeries(ScheduledEvent? e)
    {
        EventSeriesDays.Children.Clear();
        var language = CultureInfo.CurrentUICulture; // the program's language, as the rest of the window
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)first + i) % 7);
            string current = e?.Series.FirstOrDefault(f => f.Date == null && f.Day == day)?.Path ?? "";
            EventSeriesDays.Children.Add(SeriesRow(FirstUpper(language.DateTimeFormat.GetDayName(day), language), current, false,
                path => Edit<ScheduledEvent>(EventList, ev =>
                {
                    // Changed where it is, so typing and undoing it leaves the series as it was (no "unsaved changes").
                    var file = ev.Series.FirstOrDefault(f => f.Date == null && f.Day == day);
                    if (path.Length == 0) ev.Series.RemoveAll(f => f.Date == null && f.Day == day);
                    else if (file != null) file.Path = path;
                    else ev.Series.Add(new SeriesFile { Day = day, Path = path });
                }), null));
        }
        ShowSeriesDates(e);
    }

    void ShowSeriesDates(ScheduledEvent? e, DateTime? focus = null)
    {
        EventSeriesDates.Children.Clear();
        EventSeriesPastButton.IsVisible = e?.Series.Any(f => f.Date < DateTime.Today) == true;
        if (e == null) return;
        TextBox? toFocus = null;
        foreach (var file in e.Series.Where(f => f.Date != null).OrderBy(f => f.Date).ToList())
        {
            var date = file.Date!.Value;
            var row = SeriesRow(date.ToString("ddd d MMM yyyy", CultureInfo.CurrentUICulture), file.Path, date < DateTime.Today,
                path => Edit<ScheduledEvent>(EventList, _ => file.Path = path),
                () =>
                {
                    Edit<ScheduledEvent>(EventList, ev => ev.Series.Remove(file));
                    ShowSeriesDates(e);
                });
            if (date == focus) toFocus = row.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
            EventSeriesDates.Children.Add(row);
        }
        toFocus?.Focus();
    }

    /// <summary>"segunda-feira" → "Segunda-feira": only the first letter (title case would make it "Segunda-Feira").</summary>
    static string FirstUpper(string text, CultureInfo culture) => text.Length > 0 ? char.ToUpper(text[0], culture) + text[1..] : text;

    /// <summary>A day (or date) with its file: typed, or chosen as a file or folder; <paramref name="remove"/> for a date.</summary>
    Control SeriesRow(string label, string path, bool past, Action<string> changed, Action? remove)
    {
        var box = new TextBox { Text = path, PlaceholderText = remove == null ? L.T("As above") : L.T("Empty: nothing on this day") };
        box.TextChanged += (_, _) =>
        {
            changed(box.Text?.Trim() ?? "");
            ShowEventPathKind(); // a stream or a folder in the series: their settings shown
        };
        var file = new Button { Content = L.T("File…") };
        file.Click += async (_, _) => { if (await PickFile(L.F("Series: file for {0}", label)) is { } p) box.Text = p; };
        var folder = new Button { Content = L.T("Folder…") };
        folder.Click += async (_, _) => { if (await PickFolder(L.F("Series: folder whose newest file plays on {0}", label)) is { } p) box.Text = p; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        buttons.Children.Add(file);
        buttons.Children.Add(folder);
        if (remove != null)
        {
            var delete = new Button { Content = "✕" };
            ToolTip.SetTip(delete, L.T("Remove this date"));
            delete.Click += (_, _) => remove();
            buttons.Children.Add(delete);
        }
        var name = new TextBlock
        {
            Text = past ? L.F("{0} (past)", label) : label, Width = 130, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Opacity = past ? 0.55 : 1,
        };
        var row = new DockPanel();
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(name);
        row.Children.Add(buttons);
        row.Children.Add(box);
        return row;
    }
}
