using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Hour templates: seasons. A template can belong to a season (summer, Christmas): on its days it plays
/// at its hours instead of the all-year template there. The calendar can show the week as it plays in a season.
/// </summary>
public partial class ScheduleWindow
{
    /// <summary>The season the template calendar shows (null: all year).</summary>
    Season? _seasonView;

    static string SeasonName(Season s) => string.IsNullOrWhiteSpace(s.Name) ? L.T("Season") : s.Name.Trim();

    /// <summary>"1 Jul – 31 Aug", in the language of the screens.</summary>
    static string SeasonDays(Season s) => $"{Day(s.From)} – {Day(s.To)}";

    static string Day(string monthDay) =>
        DateTime.TryParseExact("2000-" + monthDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMM", CultureInfo.CurrentUICulture) : monthDay;

    void WireSeasons()
    {
        _templateCalendar.Shows = (e, day, time) =>
        {
            if (e is not PlaylistTemplate t) return true;
            var season = _data.SeasonOf(t);
            if (_seasonView == null) return season == null;
            // In a season: its templates, and the all-year ones where none of its templates has the hour.
            return season == _seasonView
                   || season == null && !_data.Templates.Any(o => o.Enabled && o.Lines.Count > 0 && _data.SeasonOf(o) == _seasonView
                                                              && o.Slots.Any(x => x.Day == day && x.At.Hours == time.Hours));
        };
        SeasonViewBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            int i = SeasonViewBox.SelectedIndex;
            _seasonView = i > 0 && i <= _data.Seasons.Count ? _data.Seasons[i - 1] : null;
            _templateCalendar.Rebuild();
        };
        TemplateSeasonBox.SelectionChanged += (_, _) =>
        {
            int i = TemplateSeasonBox.SelectedIndex;
            Edit<PlaylistTemplate>(TemplateList, t => t.SeasonId = i > 0 && i <= _data.Seasons.Count ? _data.Seasons[i - 1].Id : null);
            _templateCalendar.Rebuild();
        };
        SeasonsButton.Click += async (_, _) => await EditSeasons();
        ShowSeasonChoices();
    }

    /// <summary>The season lists (the calendar's view and the template's season), from the seasons there are now.</summary>
    void ShowSeasonChoices()
    {
        bool loading = _loading;
        _loading = true;
        var names = _data.Seasons.Select(s => $"{SeasonName(s)}  ({SeasonDays(s)})").ToList();
        SeasonViewBox.ItemsSource = new[] { L.T("All year") }.Concat(names).ToList();
        _seasonView = _seasonView == null ? null : _data.Seasons.FirstOrDefault(x => x.Id == _seasonView.Id); // the same season, edited
        SeasonViewBox.SelectedIndex = _seasonView == null ? 0 : _data.Seasons.IndexOf(_seasonView) + 1;
        TemplateSeasonBox.ItemsSource = new[] { L.T("All year") }.Concat(names).ToList();
        _loading = loading;
        ShowTemplateSeason(SelectedTemplate);
    }

    void ShowTemplateSeason(PlaylistTemplate? t)
    {
        bool loading = _loading;
        _loading = true;
        TemplateSeasonBox.SelectedIndex = t != null && _data.SeasonOf(t) is { } season ? _data.Seasons.IndexOf(season) + 1 : 0;
        _loading = loading;
    }

    /// <summary>The seasons: a name and the days of the year (every year; one may run over the new year).</summary>
    async Task EditSeasons()
    {
        var seasons = _data.Seasons.Select(s => new Season { Id = s.Id, Name = s.Name, From = s.From, To = s.To }).ToList();
        var rows = new StackPanel { Spacing = 8 };
        var months = Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentUICulture.DateTimeFormat.GetAbbreviatedMonthName(m)).ToList();
        var days = Enumerable.Range(1, 31).Select(d => d.ToString(CultureInfo.InvariantCulture)).ToList();

        Control DatePicker(string monthDay, Action<string> set)
        {
            int month = int.TryParse(monthDay[..2], out var m) ? m : 1, day = int.TryParse(monthDay[3..], out var d) ? d : 1;
            var dayBox = new ComboBox { ItemsSource = days, SelectedIndex = Math.Clamp(day, 1, 31) - 1, Width = 70 };
            var monthBox = new ComboBox { ItemsSource = months, SelectedIndex = Math.Clamp(month, 1, 12) - 1, Width = 90 };
            void Changed()
            {
                int days = DateTime.DaysInMonth(2000, monthBox.SelectedIndex + 1);
                if (dayBox.SelectedIndex + 1 > days) { dayBox.SelectedIndex = days - 1; return; } // 31 February: the 29th, shown
                set($"{monthBox.SelectedIndex + 1:00}-{dayBox.SelectedIndex + 1:00}");
            }
            dayBox.SelectionChanged += (_, _) => Changed();
            monthBox.SelectionChanged += (_, _) => Changed();
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { dayBox, monthBox } };
        }

        void Show()
        {
            rows.Children.Clear();
            if (seasons.Count == 0)
                rows.Children.Add(new TextBlock { Text = L.T("No seasons yet. A season's templates play on its days instead of the all-year ones."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            foreach (var season in seasons)
            {
                var name = new TextBox { Text = season.Name, PlaceholderText = L.T("e.g. Summer"), Width = 170 };
                name.TextChanged += (_, _) => season.Name = name.Text ?? "";
                var remove = new Button { Content = "✕" };
                ToolTip.SetTip(remove, L.T("Delete this season (its templates are switched off)"));
                remove.Click += (_, _) => { seasons.Remove(season); Show(); };
                rows.Children.Add(new WrapPanel
                {
                    ItemSpacing = 8, LineSpacing = 6,
                    Children =
                    {
                        name,
                        new TextBlock { Text = L.T("from"), VerticalAlignment = VerticalAlignment.Center },
                        DatePicker(season.From, v => season.From = v),
                        new TextBlock { Text = L.T("until (season)"), VerticalAlignment = VerticalAlignment.Center },
                        DatePicker(season.To, v => season.To = v),
                        remove,
                    },
                });
            }
        }
        Show();

        var add = new Button { Content = L.T("Add a season") };
        add.Click += (_, _) => { seasons.Add(new Season { Name = seasons.Count == 0 ? L.T("Summer") : "" }); Show(); };
        var ok = new Button { Content = L.T("OK"), IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var window = new Window
        {
            Title = L.T("Seasons"), Width = 620, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = L.T("Every year, from the first day to the last (a season may run over the new year, e.g. 15 Dec to 6 Jan). An hour template given a season plays on its days instead of the all-year template at the same hours."),
                            TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                        },
                        rows,
                        add,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
                    },
                },
            },
        };
        ok.Click += (_, _) => window.Close(true);
        cancel.Click += (_, _) => window.Close(false);
        if (!await window.ShowDialog<bool>(this)) return;

        _data.Seasons = seasons;
        // Templates of a deleted season: switched off (all year, they would play at the same hours as the all-year ones).
        var orphans = _data.Templates.Where(t => t.SeasonId is { } id && seasons.All(s => s.Id != id)).ToList();
        foreach (var t in orphans)
        {
            t.SeasonId = null;
            t.Enabled = false;
        }
        ErrorText.Text = SavedText.Text = "";
        if (orphans.Count > 0)
            ErrorText.Text = L.F("Switched off, their season deleted: {0}. Give them another season, or switch them on to play all year.",
                string.Join(", ", orphans.Select(TemplateName)));
        ShowSeasonChoices();
        foreach (var row in _templateRows) row.Refresh();
        _templateCalendar.Rebuild();
    }
}
