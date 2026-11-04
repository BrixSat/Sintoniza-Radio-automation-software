using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// A day's daily playlists looked over ahead of time (tomorrow's, for Sintoniza Pro's Home and the alerts): hours with
/// an hour template but no daily playlist, hours that came out empty or short (fill music plays the rest), and the
/// files the day plays that aren't there, in the playlists and in the ad breaks and events; and its shows (two on at
/// the same time, a live one without a presenter).
/// </summary>
public sealed class PlanCheck
{
    /// <summary>An hour this much shorter than it should be is reported.</summary>
    public static readonly TimeSpan ShortBy = TimeSpan.FromMinutes(3);
    const int Named = 4;

    /// <summary>Hours that have an hour template and no daily playlist.</summary>
    public List<DateTime> Missing { get; } = new();
    /// <summary>Hours made with nothing in them (their template found no music).</summary>
    public List<DateTime> Empty { get; } = new();
    /// <summary>Hours shorter than they should be, and by how much.</summary>
    public List<(DateTime Hour, TimeSpan By)> Short { get; } = new();
    /// <summary>Files of the daily playlists that aren't there: when they play, and what.</summary>
    public List<(DateTime At, string What)> NotFound { get; } = new();
    /// <summary>What the ad breaks and events will leave out (a file not found, nothing in a series for the day…), as the playout says it.</summary>
    public List<(DateTime At, string What)> Entries { get; } = new();
    /// <summary>What is wrong with the day's shows (<see cref="ShowCheck"/>).</summary>
    public List<string> Shows { get; } = new();
    /// <summary>None of the day's files can be reached from this computer (a share down, its paths not set): not listed one by one.</summary>
    public bool FilesUnseen { get; private set; }
    /// <summary>How many files the day plays that were looked for.</summary>
    public int FilesChecked { get; private set; }
    /// <summary>The day has hour templates (else there is nothing to check: fill music plays).</summary>
    public int Planned { get; private set; }

    public bool AllGood => Missing.Count == 0 && Empty.Count == 0 && Short.Count == 0 && NotFound.Count == 0 && Entries.Count == 0 && Shows.Count == 0 && !FilesUnseen;

    /// <param name="exists">Whether a file of the library is there (as this computer sees it).</param>
    public static PlanCheck Day(LibraryDb db, ScheduleData data, DateTime day, PlaylistGenerator.Options options, Func<string, bool>? exists = null)
    {
        exists ??= SharedPaths.FileExists;
        day = day.Date;
        var check = new PlanCheck();
        var store = new PlanStore(db);
        var hours = store.Hours(day, day.AddDays(1));
        var items = store.Items(day, day.AddDays(1));
        var generator = new PlaylistGenerator(db, data, options);
        int files = 0, missingFiles = 0;
        var notFound = new List<(DateTime, string)>();
        var said = new List<(DateTime, string)>();
        Func<long, IReadOnlySet<string>, DateTime, (string Path, TrackKind Kind)?>? picker = null;
        void Look(DateTime at, string path, string what)
        {
            if (path.Length == 0 || Takes.IsTake(path) || path.StartsWith(Pause.Path, StringComparison.Ordinal) || PlayoutEngine.IsUrl(path)) return;
            files++;
            if (exists(path)) return;
            missingFiles++;
            notFound.Add((at, what));
        }

        for (int h = 0; h < 24; h++)
        {
            var hour = day.AddHours(h);
            var entries = generator.Entries(hour);
            // As the playout will make them: what it would say it leaves out (a break with no adverts booked is Home's own
            // warning). Each part it found, and each it says it left out, counts as a file looked for.
            foreach (var entry in entries)
            {
                string unbooked = L.F("{0}: no adverts booked, skipped", entry.Label);
                int before = said.Count;
                var found = ScheduleItems.For(data, entry.Entry, entry.At, text => { if (text != unbooked) said.Add((entry.At, text)); },
                    picker ??= ScheduleItems.CategoryPicker(db), ahead: true);
                files += found.Count(i => i.StreamName == null) + said.Count - before;
                missingFiles += said.Count - before;
            }
            var template = data.TemplateAt(hour);
            if (template == null) continue;
            check.Planned++;
            // Not made, or made for another template (the playout makes it again within minutes, unless edited by hand).
            if (!hours.TryGetValue(hour, out var made) || !made.Edited && made.Signature != PlaylistGenerator.Signature(template))
            {
                check.Missing.Add(hour);
                continue;
            }
            var list = items.GetValueOrDefault(hour) ?? new();
            if (list.Count == 0) { check.Empty.Add(hour); continue; }
            // What the hour should fill: the hour less its breaks and events (as it was made).
            double target = 3600 - entries.Sum(e => e.Seconds);
            double planned = list.Sum(i => i.Seconds);
            if (target - planned > ShortBy.TotalSeconds) check.Short.Add((hour, TimeSpan.FromSeconds(target - planned)));
            var at = hour;
            foreach (var item in list)
            {
                Look(at, item.Path, item.Display.Length > 0 ? item.Display : Path.GetFileNameWithoutExtension(item.Path));
                at = at.AddSeconds(Math.Max(0, item.Seconds));
            }
        }
        check.Shows.AddRange(ShowCheck.Problems(data, day, 1, presenters: false));
        check.FilesChecked = files;
        // Not one file there: this computer doesn't reach the music (a share down, or its paths not set), not a day of
        // missing files.
        if (files >= 5 && missingFiles == files) check.FilesUnseen = true;
        else
        {
            check.NotFound.AddRange(notFound);
            check.Entries.AddRange(said.Distinct());
        }
        return check;
    }

    /// <summary>What to tell, a line for each kind of problem ("Tomorrow: …"); empty when all is well. <paramref name="shows"/>: also the shows' problems (Pro's Home lists those for the week).</summary>
    public List<string> Lines(string when, bool shows = true)
    {
        static string Hours(IEnumerable<DateTime> hours) => List(hours.Select(h => h.ToString("HH:mm")).ToList());
        static string List(IReadOnlyList<string> all) => string.Join(", ", all.Take(Named)) + (all.Count > Named ? L.F(" and {0} more", all.Count - Named) : "");
        var lines = new List<string>();
        if (Missing.Count > 0) lines.Add(L.F("{0}: no daily playlist yet for {1} (Daily playlists makes them)", when, Hours(Missing)));
        if (Empty.Count > 0) lines.Add(L.F("{0}: nothing in {1}: its template found no music (fill music plays)", when, Hours(Empty)));
        if (Short.Count > 0)
            lines.Add(L.F("{0}: short hours, fill music plays the rest: {1}", when,
                List(Short.Select(s => L.F("{0:HH:mm} ({1:0} min)", s.Hour, s.By.TotalMinutes)).ToList())));
        if (NotFound.Count > 0)
            lines.Add(L.F("{0}: {1} not found: {2}", when, NotFound.Count == 1 ? L.T("a file") : L.F("{0} files", NotFound.Count),
                List(NotFound.Select(n => L.F("{0} ({1:HH:mm})", n.What, n.At)).ToList())));
        if (Entries.Count > 0)
            lines.Add(L.F("{0}, ad breaks and events: {1}", when, List(Entries.Select(e => e.What).ToList())));
        if (shows) lines.AddRange(Shows.Select(s => L.F("{0}: {1}", when, s)));
        if (FilesUnseen) lines.Add(L.F("{0}: none of its {1} files can be reached from this computer (the music share, or this computer's paths)", when, FilesChecked));
        return lines;
    }
}
