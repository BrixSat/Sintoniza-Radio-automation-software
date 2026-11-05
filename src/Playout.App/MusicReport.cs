using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>A line of the music report: a number and what it counts.</summary>
public sealed record ReportLine(string Count, string Text);

/// <summary>
/// Sintoniza Pro → Library → Music report, for a period: the songs and artists played most (the as-run log: what really
/// went on air), each category's share of the songs played, and how many of the hours that went by kept each of the
/// hour templates' targets (by what played in them).
/// </summary>
public sealed class MusicReport
{
    public int Plays { get; private init; }
    public List<ReportLine> Songs { get; private init; } = new();
    public List<ReportLine> Artists { get; private init; } = new();
    public List<ReportLine> Categories { get; private init; } = new();
    public List<ReportLine> Targets { get; private init; } = new();

    /// <summary>The report for the days from <paramref name="from"/> to <paramref name="to"/> (the station's time, both included).</summary>
    public static MusicReport Make(LibraryDb db, ScheduleData data, DateTime from, DateTime to)
    {
        var music = AsRunDb.Read(db, from.Date, to.Date).Where(e => e.Type == "Music").ToList();
        static string Song(AsRunEntry e) => e.Artist.Trim().Length > 0 ? $"{e.Artist.Trim()} – {e.Title.Trim()}" : e.Title.Trim();
        var songs = music.GroupBy(e => e.File.Length > 0 ? e.File : Song(e))
            .Select(g => (Count: g.Count(), Text: Song(g.Last())))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Text).Take(25)
            .Select(x => new ReportLine(x.Count.ToString(), x.Text)).ToList();
        var artists = music.Where(e => e.Artist.Trim().Length > 0)
            .GroupBy(e => LibraryDb.Normalize(e.Artist).Trim())
            .Select(g => (Count: g.Count(), Text: g.Last().Artist.Trim()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Text).Take(15)
            .Select(x => new ReportLine(x.Count.ToString(), x.Text)).ToList();

        // Each category's share: by the song's own category in the library (songs not in it: "not in the library"). The
        // as-run log has the file as the computer on air played it: the library's path, whatever the case of its letters.
        var tracks = db.TracksByPath(music.Where(e => e.File.Length > 0).Select(e => SharedPaths.ToLibrary(e.File)))
            .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        ReportTrack? TrackOf(AsRunEntry e) => e.File.Length > 0 && tracks.TryGetValue(SharedPaths.ToLibrary(e.File), out var t) ? t : null;
        var all = db.Categories();
        var names = new Dictionary<long, string>();
        string CategoryOf(AsRunEntry e) => TrackOf(e)?.CategoryId is { } id && all.Any(c => c.Id == id)
            ? names.TryGetValue(id, out var n) ? n : names[id] = CategoryTree.PathOf(all, id)
            : L.T("(not in the library)");
        var categories = music.GroupBy(CategoryOf)
            .Select(g => (Count: g.Count(), Text: g.Key)).OrderByDescending(x => x.Count)
            .Select(x => new ReportLine(x.Count.ToString(), L.F("{0} ({1:0}%)", x.Text, 100.0 * x.Count / Math.Max(1, music.Count)))).ToList();

        return new MusicReport
        {
            Plays = music.Count, Songs = songs, Artists = artists, Categories = categories,
            Targets = TargetLines(db, data, music, TrackOf, StationTime.Now),
        };
    }

    /// <summary>
    /// For each hour template's target: of the hours of the period that template was on (as the schedule has it now)
    /// and that have gone by, how many kept it — by what really played in them (the as-run log), so any period counts.
    /// </summary>
    static List<ReportLine> TargetLines(LibraryDb db, ScheduleData data, List<AsRunEntry> music, Func<AsRunEntry, ReportTrack?> track, DateTime now)
    {
        if (!data.Templates.Any(t => t.Targets.Count > 0)) return new();
        var thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var byHour = music.Where(e => e.Start < thisHour)
            .GroupBy(e => new DateTime(e.Start.Year, e.Start.Month, e.Start.Day, e.Start.Hour, 0, 0))
            .Select(g => (Hour: g.Key, Template: data.TemplateAt(g.Key), Songs: g.OrderBy(e => e.Start).Select(e => track(e)?.Id).ToList()))
            .Where(h => h.Template is { Targets.Count: > 0 }).ToList();
        var members = new Dictionary<long, HashSet<long>>();
        HashSet<long> Songs(long category) => members.TryGetValue(category, out var s) ? s : members[category] = db.TrackIdsIn([category]);
        var lines = new List<ReportLine>();
        foreach (var template in data.Templates.Where(t => t.Targets.Count > 0))
            foreach (var target in template.Targets.Where(x => x.CategoryId > 0))
            {
                var songs = Songs(target.CategoryId);
                var hours = byHour.Where(h => h.Template == template).ToList();
                if (hours.Count == 0) continue;
                int kept = hours.Count(h =>
                {
                    int count = h.Songs.Count(id => id is { } i && songs.Contains(i));
                    int run = 0, most = 0;
                    foreach (var id in h.Songs)
                    {
                        run = id is { } i && songs.Contains(i) ? run + 1 : 0;
                        most = Math.Max(most, run);
                    }
                    return target.Kind switch
                    {
                        TargetKind.AtLeast => count >= target.Count,
                        TargetKind.AtMost => count <= target.Count,
                        _ => most <= Math.Max(1, target.Count),
                    };
                });
                string what = target.Kind switch
                {
                    TargetKind.AtLeast => L.F("at least {0} an hour", target.Count),
                    TargetKind.AtMost => L.F("at most {0} an hour", target.Count),
                    _ => L.F("at most {0} in a row", target.Count),
                };
                lines.Add(new ReportLine($"{100.0 * kept / hours.Count:0}%", L.F("{0} · {1} of {2}: {3} of {4} hours", template.Name.Trim(), what, target.CategoryName, kept, hours.Count)));
            }
        return lines;
    }

    /// <summary>The songs of a category (and the ones under it) not played for <paramref name="days"/> days, or never: the longest first.</summary>
    public static (int Count, List<ReportLine> Lines) NotPlayed(LibraryDb db, long category, int days, DateTime now)
    {
        var since = StationTime.ToUtc(now.AddDays(-days));
        var rested = db.RotationTracks([category]).Where(t => t.LastPlayed is not { } last || last < since)
            .OrderBy(t => t.LastPlayed ?? DateTime.MinValue).ToList();
        var lines = rested.Take(100).Select(t => new ReportLine(
            t.LastPlayed is { } last ? StationTime.FromUtc(last).ToString("dd'/'MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture) : L.T("never"),
            t.Artist.Trim().Length > 0 ? $"{t.Artist.Trim()} – {t.Title.Trim()}" : Path.GetFileNameWithoutExtension(t.Path))).ToList();
        return (rested.Count, lines);
    }
}
