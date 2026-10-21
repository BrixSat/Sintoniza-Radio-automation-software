using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// When one or more songs may be picked automatically: between two dates, on some days, at some hours, at most so
/// many times a day and a week. Closes with true when saved (for every song chosen).
/// </summary>
public partial class RestrictionsWindow : Window
{
    readonly LibraryDb _db = null!;
    readonly List<long> _ids = new();
    readonly List<ToggleButton> _days = new(), _hours = new();

    // Parameterless constructor for the XAML previewer.
    public RestrictionsWindow() => InitializeComponent();

    public RestrictionsWindow(LibraryDb db, IReadOnlyCollection<long> trackIds)
    {
        InitializeComponent();
        Localization.Apply(this);
        _db = db;
        _ids = trackIds.ToList();
        var tracks = db.GetByIds(_ids);
        SongsText.Text = tracks.Count == 1
            ? (string.IsNullOrWhiteSpace(tracks[0].Artist) ? tracks[0].Title : $"{tracks[0].Artist} - {tracks[0].Title}")
            : L.F("{0} songs", tracks.Count) + (tracks.Select(t => t.Restrictions).Distinct().Count() > 1 ? " · " + L.T("they differ: saving sets these for all") : "");

        var culture = CultureInfo.CurrentCulture;
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)((i + 1) % 7); // Monday first
            var b = new ToggleButton { Content = culture.DateTimeFormat.GetAbbreviatedDayName(day), IsChecked = true };
            b.Classes.Add("slot");
            _days.Add(b);
            DayGrid.Children.Add(b);
        }
        for (int h = 0; h < 24; h++)
        {
            var b = new ToggleButton { Content = $"{h:00}", IsChecked = true };
            b.Classes.Add("slot");
            _hours.Add(b);
            HourGrid.Children.Add(b);
        }
        AllHoursButton.Click += (_, _) => SetHours(_ => true);
        DayHoursButton.Click += (_, _) => SetHours(h => h is >= 7 and < 20);
        NightHoursButton.Click += (_, _) => SetHours(h => h is >= 20 or < 7);
        ClearFromButton.Click += (_, _) => FromBox.SelectedDate = null;
        ClearUntilButton.Click += (_, _) => UntilBox.SelectedDate = null;
        RemoveButton.Click += (_, _) => Show(null);
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += async (_, _) => await Save();
        Show(tracks.FirstOrDefault()?.Restrictions);
    }

    void SetHours(Func<int, bool> on)
    {
        for (int h = 0; h < 24; h++) _hours[h].IsChecked = on(h);
    }

    void Show(SongRestrictions? r)
    {
        FromBox.SelectedDate = r?.From;
        UntilBox.SelectedDate = r?.Until;
        for (int i = 0; i < 7; i++) _days[i].IsChecked = r?.Days is not { } d || d[i] == '1';
        SetHours(h => r?.Hours is not { } hours || hours[h] == '1');
        MaxDayBox.Value = r?.MaxPerDay ?? 0;
        MaxWeekBox.Value = r?.MaxPerWeek ?? 0;
        ErrorText.Text = "";
    }

    async Task Save()
    {
        var days = _days.Select(b => b.IsChecked == true).ToList();
        var hours = _hours.Select(b => b.IsChecked == true).ToList();
        if (!days.Any(x => x) || !hours.Any(x => x))
        {
            ErrorText.Text = L.T("Leave at least one day and one hour (or remove the restrictions).");
            return;
        }
        if (FromBox.SelectedDate is { } from && UntilBox.SelectedDate is { } until && until < from)
        {
            ErrorText.Text = L.T("The last day is before the first.");
            return;
        }
        int maxDay = (int)(MaxDayBox.Value ?? 0), maxWeek = (int)(MaxWeekBox.Value ?? 0);
        var r = new SongRestrictions(FromBox.SelectedDate?.Date, UntilBox.SelectedDate?.Date, SongRestrictions.Mask(days), SongRestrictions.Mask(hours),
            maxDay > 0 ? maxDay : null, maxWeek > 0 ? maxWeek : null);
        SaveButton.IsEnabled = false;
        try
        {
            await Task.Run(() => _db.SetRestrictions(_ids, r.IsEmpty ? null : r));
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = L.F("Not saved: {0}", ex.Message);
            SaveButton.IsEnabled = true;
        }
    }
}
