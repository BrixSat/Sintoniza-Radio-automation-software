using Playout.Engine;
namespace Playout.App;

/// <summary>Sintoniza Pro → Shows: the shows in the week, live or automated, with their presenter, length and notes, and what is wrong with them.</summary>
public partial class ScheduleWindow
{
    EntryCalendar? _showCalendar;
    List<ScheduleRow> _showRows = new();

    LiveShow? SelectedShow => (ShowList.SelectedItem as ScheduleRow)?.Item as LiveShow;

    void WireShows()
    {
        _showCalendar = new EntryCalendar(this, ShowGrid, ShowHeader, ShowStepBox, ShowOffsetBox, ShowAddTimeBox, ShowAddTimeButton, ShowClearButton,
            () => _data.Shows, () => SelectedShow, () => { (ShowList.SelectedItem as ScheduleRow)?.Refresh(); ShowProblems(); });
        ShowStepBox.SelectedIndex = Array.IndexOf(Steps, 30) is var half and >= 0 ? half : 1; // shows start on the hour or the half hour
        ShowList.SelectionChanged += (_, _) => ShowShow();
        AddShowButton.Click += (_, _) =>
        {
            var s = new LiveShow { Name = L.T("Morning show") };
            _data.Shows.Add(s);
            _showRows = _showRows.Append(ShowRow(s)).ToList();
            ShowList.ItemsSource = _showRows;
            ShowList.SelectedIndex = _showRows.Count - 1;
            ShowNameBox.Focus();
            ShowNameBox.SelectAll();
        };
        DeleteShowButton.Click += async (_, _) =>
        {
            if (ShowList.SelectedItem is not ScheduleRow { Item: LiveShow s } row) return;
            if (!await Dialogs.Confirm(this, L.T("Delete show"), L.F("Delete \"{0}\"?", EntryName(s)), L.T("Delete"))) return;
            _data.Shows.Remove(s);
            int i = _showRows.IndexOf(row);
            _showRows = _showRows.Where(r => r != row).ToList();
            ShowList.ItemsSource = _showRows;
            ShowList.SelectedIndex = Math.Min(i, _showRows.Count - 1);
            _showCalendar.Rebuild();
            ShowProblems();
        };
        ShowNameBox.TextChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Name = ShowNameBox.Text?.Trim() ?? "");
        ShowEnabledBox.IsCheckedChanged += (_, _) => { Edit<LiveShow>(ShowList, s => s.Enabled = ShowEnabledBox.IsChecked == true); ShowProblems(); };
        ShowPresenterBox.TextChanged += (_, _) => { Edit<LiveShow>(ShowList, s => s.Presenter = ShowPresenterBox.Text?.Trim() ?? ""); ShowProblems(); };
        ShowMinutesBox.ValueChanged += (_, _) => { Edit<LiveShow>(ShowList, s => s.Minutes = (double)(ShowMinutesBox.Value ?? 60)); ShowProblems(); };
        ShowNotesBox.TextChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Notes = ShowNotesBox.Text ?? "");
        ShowLiveRadio.IsCheckedChanged += (_, _) => { Edit<LiveShow>(ShowList, s => s.Live = ShowLiveRadio.IsChecked == true); ShowProblems(); };
    }

    /// <summary>Over the show shows that start again before they end or are on at the same time, live ones without a presenter, and how long the live ones are on.</summary>
    void ShowProblems()
    {
        var today = StationTime.Today;
        var problems = ShowCheck.Problems(_data, today, 7);
        ShowWarnings.Text = string.Join("\n", problems.Select(p => "⚠ " + p));
        ShowWarnings.IsVisible = problems.Count > 0;
        double hours = ShowCheck.LiveHours(_data, today, 7);
        ShowLiveHours.Text = hours > 0
            ? L.F("Live shows: up to {0:0.#} hours a day (ASSISTED while the presenter is there)", hours)
            : L.T("No live shows: AUTO all day.");
    }

    void LoadShows(ScheduleData data)
    {
        _showRows = data.Shows.Select(ShowRow).ToList();
        ShowList.ItemsSource = _showRows;
        ShowList.SelectedIndex = _showRows.Count > 0 ? 0 : -1;
        ShowShow();
    }

    void ShowShow()
    {
        var s = SelectedShow;
        ShowPanel.IsEnabled = s != null;
        _loading = true;
        ShowNameBox.Text = s?.Name ?? "";
        ShowEnabledBox.IsChecked = s?.Enabled ?? true;
        ShowPresenterBox.Text = s?.Presenter ?? "";
        ShowMinutesBox.Value = (decimal)(s?.Minutes ?? 60);
        ShowNotesBox.Text = s?.Notes ?? "";
        ShowLiveRadio.IsChecked = s?.Live ?? true;
        ShowAutomatedRadio.IsChecked = !(s?.Live ?? true);
        _loading = false;
        _showCalendar?.Rebuild();
        ShowProblems();
    }

    ScheduleRow ShowRow(LiveShow s) => new(s, () =>
    {
        string presenter = !s.Live ? L.T("automated") + " · " : s.Presenter.Trim().Length > 0 ? s.Presenter.Trim() + " · " : "";
        return (EntryName(s), $"{presenter}{SlotsSummary(s)} · {Plural((int)s.Minutes, "{0} minute", "{0} minutes")}", s.Enabled);
    });
}
