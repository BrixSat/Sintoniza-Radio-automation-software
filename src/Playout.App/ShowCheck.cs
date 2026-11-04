namespace Playout.App;

/// <summary>
/// The shows looked over (Sintoniza Pro's Shows page and Home, tomorrow's check): a show that starts again before it
/// ends, two shows at the same time, a live show with no presenter, and how long the live ones keep the playout in
/// ASSISTED.
/// </summary>
public static class ShowCheck
{
    /// <summary>
    /// What is wrong with the shows on the days from <paramref name="from"/>, a line each (once per show or pair).
    /// <paramref name="presenters"/>: also live shows without a presenter (a choice in Pro, not something for tomorrow's alert).
    /// </summary>
    public static List<string> Problems(ScheduleData data, DateTime from, int days, bool presenters = true)
    {
        var lines = new List<string>();
        var shows = data.Shows.Where(s => s.Enabled && s.Minutes > 0).ToList();
        var times = shows.ToDictionary(s => s, s => Times(s, from, days));
        foreach (var show in shows)
        {
            var list = times[show];
            for (int i = 1; i < list.Count; i++)
                if (list[i] >= from.Date && list[i] < list[i - 1].AddMinutes(show.Minutes))
                {
                    lines.Add(L.F("Show {0}: it starts again at {1:ddd HH:mm}, before the one at {2:HH:mm} ends ({3:0} minutes from each start): one longer show does it",
                        Name(show), list[i], list[i - 1], show.Minutes));
                    break;
                }
        }
        for (int a = 0; a < shows.Count; a++)
            for (int b = a + 1; b < shows.Count; b++)
                if (Overlap(shows[a], times[shows[a]], shows[b], times[shows[b]], from.Date) is { } at)
                    lines.Add(L.F("Shows {0} and {1} are on at the same time ({2:ddd HH:mm}): the one that starts last is on air", Name(shows[a]), Name(shows[b]), at));
        foreach (var show in shows.Where(s => presenters && s.Live && s.Presenter.Trim().Length == 0 && times[s].Any(t => t >= from.Date)))
            lines.Add(L.F("Show {0} is live but has no presenter (or make it automated)", Name(show)));
        return lines;
    }

    /// <summary>The most hours a day, of the days from <paramref name="from"/>, that live shows are on (ASSISTED once the presenter says they are there).</summary>
    public static double LiveHours(ScheduleData data, DateTime from, int days)
    {
        double most = 0;
        for (int d = 0; d < days; d++)
        {
            var day = from.Date.AddDays(d);
            var spans = data.Shows.Where(s => s.Enabled && s.Live && s.Minutes > 0)
                .SelectMany(s => s.TimesOn(day.AddDays(-1)).Concat(s.TimesOn(day)).Select(t => (Start: t, End: t.AddMinutes(s.Minutes))))
                .Select(x => (Start: x.Start < day ? day : x.Start, End: x.End > day.AddDays(1) ? day.AddDays(1) : x.End))
                .Where(x => x.End > x.Start).OrderBy(x => x.Start).ToList();
            double hours = 0;
            DateTime? start = null, end = null;
            foreach (var (s, e) in spans)
            {
                if (end is { } until && s <= until) { if (e > until) end = e; continue; }
                if (start is { } a0 && end is { } b0) hours += (b0 - a0).TotalHours;
                (start, end) = (s, e);
            }
            if (start is { } a1 && end is { } b1) hours += (b1 - a1).TotalHours;
            most = Math.Max(most, hours);
        }
        return most;
    }

    static string Name(LiveShow s) => s.Name.Trim().Length > 0 ? s.Name.Trim() : L.T("Show");

    /// <summary>Its starts from the day before (a show on past midnight) to the last day.</summary>
    static List<DateTime> Times(LiveShow show, DateTime from, int days) =>
        Enumerable.Range(-1, days + 1).SelectMany(d => show.TimesOn(from.Date.AddDays(d))).Distinct().Order().ToList();

    /// <summary>When two shows are first on at the same time (from <paramref name="from"/> on), if they are.</summary>
    static DateTime? Overlap(LiveShow a, List<DateTime> aTimes, LiveShow b, List<DateTime> bTimes, DateTime from)
    {
        // Both in order: step past whichever ends first (no pairing of every time with every other).
        for (int i = 0, j = 0; i < aTimes.Count && j < bTimes.Count;)
        {
            DateTime x = aTimes[i], y = bTimes[j], xEnd = x.AddMinutes(a.Minutes), yEnd = y.AddMinutes(b.Minutes);
            if (x < yEnd && y < xEnd && (x > y ? x : y) is var at && at >= from) return at;
            if (xEnd <= yEnd) i++; else j++;
        }
        return null;
    }
}
