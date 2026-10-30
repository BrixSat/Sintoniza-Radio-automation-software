namespace Playout.App;

/// <summary>Sintoniza Pro → Shows: live shows in the week, with their presenter, length and notes.</summary>
public partial class ScheduleWindow
{
    EntryCalendar? _showCalendar;
    List<ScheduleRow> _showRows = new();

    LiveShow? SelectedShow => (ShowList.SelectedItem as ScheduleRow)?.Item as LiveShow;

    void WireShows()
    {
        _showCalendar = new EntryCalendar(this, ShowGrid, ShowHeader, ShowStepBox, ShowOffsetBox, ShowAddTimeBox, ShowAddTimeButton, ShowClearButton,
            () => _data.Shows, () => SelectedShow, () => (ShowList.SelectedItem as ScheduleRow)?.Refresh());
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
        };
        ShowNameBox.TextChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Name = ShowNameBox.Text?.Trim() ?? "");
        ShowEnabledBox.IsCheckedChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Enabled = ShowEnabledBox.IsChecked == true);
        ShowPresenterBox.TextChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Presenter = ShowPresenterBox.Text?.Trim() ?? "");
        ShowMinutesBox.ValueChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Minutes = (double)(ShowMinutesBox.Value ?? 60));
        ShowNotesBox.TextChanged += (_, _) => Edit<LiveShow>(ShowList, s => s.Notes = ShowNotesBox.Text ?? "");
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
        _loading = false;
        _showCalendar?.Rebuild();
    }

    ScheduleRow ShowRow(LiveShow s) => new(s, () =>
    {
        string presenter = s.Presenter.Trim().Length > 0 ? s.Presenter.Trim() + " · " : "";
        return (EntryName(s), $"{presenter}{SlotsSummary(s)} · {Plural((int)s.Minutes, "{0} minute", "{0} minutes")}", s.Enabled);
    });
}
