using System.Text.Json;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// The playout's side of the daily playlists. It keeps them made ahead (today and the next days, see
/// <see cref="AppSettings.PlanDaysAhead"/>; an hour whose template changed is made again unless it was edited by hand
/// or is about to play), loads the planned hours into the playlist about half an hour before they play, and keeps time
/// with the clock: near the end of an hour, optional items are dropped when it runs late, or a song is added when it
/// runs short, so the next hour starts on time. The playlist items it adds carry their hour (<see cref="Tag"/>); what
/// the host does with them on air doesn't change the plan.
/// </summary>
sealed class PlanPlayer
{
    /// <summary>How far ahead the planned hours are put in the playlist.</summary>
    static readonly TimeSpan Ahead = TimeSpan.FromMinutes(30);
    /// <summary>How long before the end of an hour it starts keeping time.</summary>
    static readonly TimeSpan SyncWindow = TimeSpan.FromMinutes(8);
    /// <summary>The next hour may start this late before an optional item is dropped.</summary>
    static readonly TimeSpan Late = TimeSpan.FromSeconds(20);
    /// <summary>…and this early before a song is added.</summary>
    static readonly TimeSpan Early = TimeSpan.FromSeconds(75);
    /// <summary>An hour that is about to play isn't made again when its template changes (it may be loaded already).</summary>
    static readonly TimeSpan KeepNear = TimeSpan.FromMinutes(40);
    static readonly TimeSpan KeepEvery = TimeSpan.FromMinutes(10);

    readonly PlayoutEngine _engine;
    readonly LibraryService _library;
    readonly Func<ScheduleData?> _schedule;
    readonly Func<PlaylistGenerator.Options> _options;
    readonly Func<int> _daysAhead;
    Task? _busy;
    DateTime _nextCheck, _nextKeep, _nextSync, _nextOver;
    (DateTime Hour, DateTime Until)? _pending; // an hour just loaded, until its items are in the playlist
    readonly Dictionary<DateTime, DateTime> _empty = new(); // hours with nothing planned → when to look again
    readonly HashSet<string> _reported = new();
    volatile bool _scheduleChanged;

    /// <summary>A line for the status bar and the log (raised off the UI thread).</summary>
    public event Action<string>? Message;

    public PlanPlayer(PlayoutEngine engine, LibraryService library, Func<ScheduleData?> schedule, Func<PlaylistGenerator.Options> options, Func<int> daysAhead)
    {
        _engine = engine;
        _library = library;
        _schedule = schedule;
        _options = options;
        _daysAhead = daysAhead;
    }

    /// <summary>The schedule (templates, breaks) changed: the plan is looked at again straight away.</summary>
    public void ScheduleChanged() => _scheduleChanged = true;

    /// <summary>Another computer is on air: this one neither makes, puts in nor changes the daily playlists.</summary>
    public bool Standby { get; set; }

    /// <summary>The hour now, from the item planned for now, in the playlist (this computer has just gone on air).</summary>
    public Task LoadNow()
    {
        var now = DateTime.Now;
        _pending = (Hour(now), now.AddSeconds(20));
        return _busy = Task.Run(() => Load(Hour(now), now));
    }

    // ───────────────────────── Tags ─────────────────────────

    const string Prefix = "plan:";

    /// <summary>"plan:2026-10-19T13" and ":opt" for an optional item.</summary>
    public static string Tag(DateTime hour, bool optional) => Prefix + hour.ToString("yyyy-MM-ddTHH") + (optional ? ":opt" : "");

    public static (DateTime Hour, bool Optional)? ReadTag(string? tag)
    {
        if (tag == null || !tag.StartsWith(Prefix, StringComparison.Ordinal) || tag.Length < Prefix.Length + 13) return null;
        if (!DateTime.TryParseExact(tag.AsSpan(Prefix.Length, 13), "yyyy-MM-ddTHH", null, System.Globalization.DateTimeStyles.None, out var hour)) return null;
        return (hour, tag.EndsWith(":opt", StringComparison.Ordinal));
    }

    // ───────────────────────── Every few seconds ─────────────────────────

    /// <summary>
    /// From the screen's update (UI thread): <paramref name="ends"/> is when the playlist runs out (null if not known),
    /// <paramref name="auto"/> the AUTO mode, <paramref name="rows"/> the playlist with each item's expected start.
    /// </summary>
    public void Check(DateTime? ends, bool auto, IReadOnlyList<PlaylistRow> rows)
    {
        var now = DateTime.Now;
        if (Standby || now < _nextCheck || _busy is { IsCompleted: false }) return;
        _nextCheck = now.AddSeconds(2);

        if (now >= _nextKeep || _scheduleChanged)
        {
            _scheduleChanged = false;
            _nextKeep = now + KeepEvery;
            _busy = Task.Run(() => Keep(now));
            return;
        }
        if (!auto) return;

        var hour = Hour(now);
        // The planned hours still to play in the playlist (not the item on air, nor what played).
        var waiting = rows.Where(r => !r.IsPlayed && !r.IsPlaying && !r.IsPreview).Select(r => ReadTag(r.Track.Tag)?.Hour)
            .OfType<DateTime>().ToHashSet();
        // A load still on its way to the playlist (the engine adds items on its own thread): nothing else meanwhile.
        if (_pending is { } pending)
        {
            if (waiting.Contains(pending.Hour) || now > pending.Until) _pending = null;
            else return;
        }
        // An ad break (or event) that ends at its time on the hour keeps that hour's end itself (the engine fits the
        // songs before it): nothing is added or taken out around it here, and what is left after it goes.
        var anchor = HourEndingBreak(rows);
        if (anchor is { } a) DropAfterBreak(rows, a);
        if (now >= _nextSync && hour.AddHours(1) - now <= SyncWindow && anchor?.Hour != hour.AddHours(1)) Sync(hour, rows, now);
        if (now >= _nextOver)
        {
            _nextOver = now.AddSeconds(15);
            DropOverHour(rows, now);
        }
        KeepEarlyHours(rows, now, anchor?.Hour);
        if (_busy is { IsCompleted: false }) return;

        // When the playlist runs out, the plan carries on from there: the hour it runs out in, from the item planned for
        // that moment (after Clear, the rest of this hour as planned), or, once that hour is in, the next one in full.
        // When the playlist runs out, from the same rows as the decision (the window's figure may be a moment behind
        // them, e.g. just after Clear): the last item's start and length; stopped (no start times), the lengths of what
        // is still to play.
        var left = rows.Where(r => !r.IsPlayed && !r.IsPreview).ToList();
        var end = left.LastOrDefault(r => r.StartAt != null) is { StartAt: { } lastStart } last && left[^1] == last
            ? lastStart.AddSeconds(last.Track.PlayLength)
            : now.AddSeconds(left.Sum(r => r.Track.PlayLength));
        if (end < now) end = now;
        if (end - now >= Ahead) return;
        var target = Hour(end);
        // From the moment it runs out only when nothing planned is left before it (after Clear, at start); after the
        // hour before (even one that runs over), in full: keeping time near the hour's end is Sync's job.
        DateTime? from = waiting.Contains(target.AddHours(-1)) ? null : end;
        if (waiting.Contains(target)) { target = target.AddHours(1); from = null; }
        if (waiting.Contains(target) || target > hour.AddHours(2)) return;
        // An hour with nothing planned (fill music) is looked at again every minute; the next is put in just before it
        // starts, so it starts on time.
        if (_empty.TryGetValue(target, out var again) && now < again)
        {
            target = target.AddHours(1);
            from = null;
            if (target - now > TimeSpan.FromMinutes(2) || waiting.Contains(target)) return;
        }
        var load = target;
        var start = from;
        _pending = (load, now.AddSeconds(20));
        _busy = Task.Run(() => Load(load, start));
    }

    static DateTime Hour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0);

    PlanStore? Store => _library.Db is { } db ? new PlanStore(db) : null;

    PlaylistGenerator.Options Options => _options();

    /// <summary>Makes the hours not made yet, and those whose template changed (not edited, not about to play).</summary>
    void Keep(DateTime now)
    {
        if (Store is not { } store || _schedule() is not { } data) return;
        try
        {
            int made = PlanMaker.Ensure(store, data, Options, Hour(now), now.Date.AddDays(_daysAhead() + 1), now + KeepNear, out var problems);
            foreach (var p in problems) if (_reported.Add(p)) Say(p);
            if (made > 0)
            {
                Say(made == 1 ? L.T("Daily playlists: 1 hour made") : L.F("Daily playlists: {0} hours made", made));
                lock (_empty) _empty.Clear(); // an hour that had nothing may have something now: looked at again straight away
            }
            store.Prune(now.Date.AddDays(-2));
            PlanCache.Save(store, Hour(now), Hour(now).AddHours(26));
        }
        catch (Exception ex)
        {
            if (_reported.Add("keep:" + ex.Message)) Say(L.F("Daily playlists not made: {0}", ex.Message));
        }
    }

    /// <summary>Puts a planned hour in the playlist; the hour now playing from the item planned for now.</summary>
    void Load(DateTime hour, DateTime? from)
    {
        List<PlanItem>? items = null;
        string? hourNote = null;
        try
        {
            if (Store is { } store)
            {
                var made = store.Hours(hour, hour.AddHours(1));
                if (!made.ContainsKey(hour) && _schedule() is { } data)
                {
                    PlanMaker.Ensure(store, data, Options, hour, hour.AddHours(1), DateTime.MinValue, out _);
                    made = store.Hours(hour, hour.AddHours(1));
                }
                hourNote = made.GetValueOrDefault(hour)?.Note;
                items = store.Items(hour);
            }
        }
        catch (Exception ex)
        {
            if (_reported.Add("load:" + ex.Message)) Say(L.F("Daily playlist: the database can't be reached ({0}); the last copy is used", ex.Message));
        }
        items ??= PlanCache.Items(hour);
        if (items == null || items.Count == 0)
        {
            lock (_empty) _empty[hour] = DateTime.Now.AddMinutes(1);
            _pending = null; // nothing is on its way
            if (_reported.Add("empty:" + hour)) Say(L.F("Daily playlist {0:dd/MM HH}:00: nothing planned; fill music plays", hour));
            return;
        }
        lock (_empty) _empty.Remove(hour);
        // From the middle of the hour: from the item planned for that moment.
        if (from is { } moment && moment > hour)
        {
            double offset = (moment - hour).TotalSeconds, at = 0;
            int skip = 0;
            while (skip < items.Count - 1 && at + items[skip].Seconds <= offset) at += items[skip++].Seconds;
            items = items.Skip(skip).ToList();
            // Not a voice track whose song was left behind.
            while (items.Count > 1 && items[0].FollowsPrevious) items.RemoveAt(0);
        }
        // The hour's note goes with its first item: the presenter sees it as the hour comes up.
        _engine.AddItems(items.Select((i, n) => new ScheduledItem(i.Path, i.Kind, Tag: Tag(hour, i.Optional), Overlap: i.Overlap, FollowsPrevious: i.FollowsPrevious,
            Note: Note(n == 0 ? hourNote : null, i.Note))).ToList());
        Say(L.F("Daily playlist {0:HH}:00 in the playlist: {1}", hour, L.F(items.Count == 1 ? "{0} item" : "{0} items", items.Count)));
    }

    /// <summary>The note an item carries on air: the hour's (on its first item) and its own.</summary>
    internal static string? Note(string? hourNote, string? itemNote)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(hourNote)) parts.Add(L.F("This hour: {0}", hourNote.Trim()));
        if (!string.IsNullOrWhiteSpace(itemNote)) parts.Add(itemNote.Trim());
        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    /// <summary>
    /// A planned item that would start after its hour has ended (a hand-made hour longer than the hour): it is left out
    /// so the next hour starts on time. Adverts, items of a break or event and locked items are never left out.
    /// </summary>
    public static bool OverHour(PlaylistRow row, DateTime start) =>
        ReadTag(row.Track.Tag) is { } t && start >= t.Hour.AddHours(1) + Late && Droppable(row);

    static bool Droppable(PlaylistRow row) =>
        !row.IsPreview && row.Track.Kind != TrackKind.Commercial && !row.Track.InBreak && row.Track.Lock == ItemLock.Free;

    /// <summary>
    /// From <see cref="SyncWindow"/> before an hour ends (and after): a planned item of it that would start after it
    /// ends goes, the last first, so the next hour starts on time (the item on air always plays to its end).
    /// </summary>
    void DropOverHour(IReadOnlyList<PlaylistRow> rows, DateTime now)
    {
        var over = rows.Where(r => !r.IsPlayed && !r.IsPlaying && r.StartAt is { } s && OverHour(r, s)
                                   && now >= ReadTag(r.Track.Tag)!.Value.Hour.AddHours(1) - SyncWindow).ToList();
        if (over.Count == 0) return;
        var drop = over[^1];
        var hour = ReadTag(drop.Track.Tag)!.Value.Hour;
        _engine.Remove(drop.Id);
        if (Store is { } store) _ = Task.Run(() => { try { store.KeepTime(hour, [], drop.Track.Path); } catch { /* the plan stays as it was */ } });
        Say(L.F("Daily playlist: {0} left out, past the end of {1:HH}:00", drop.Track.Display, hour));
    }

    /// <summary>
    /// Near the end of <paramref name="hour"/>: when the next hour's first item would start late, the last optional
    /// item of this hour goes; when it would start early (and none is left to drop), a song is added before it.
    /// </summary>
    void Sync(DateTime hour, IReadOnlyList<PlaylistRow> rows, DateTime now)
    {
        _nextSync = now.AddSeconds(15);
        var nextHour = hour.AddHours(1);
        var first = rows.FirstOrDefault(r => !r.IsPlayed && !r.IsPlaying && ReadTag(r.Track.Tag)?.Hour == nextHour);
        if (first?.StartAt is not { } start) return;
        var optional = rows.Where(r => !r.IsPlayed && !r.IsPlaying && ReadTag(r.Track.Tag) is { } t && t.Hour == hour && t.Optional).ToList();
        if (start - nextHour > Late && optional.Count > 0)
        {
            var drop = optional[^1];
            _engine.Remove(drop.Id);
            // The plan shows what really plays.
            if (Store is { } store) _ = Task.Run(() => { try { store.KeepTime(hour, [], drop.Track.Path); } catch { /* the plan stays as it was */ } });
            Say(L.F("Daily playlist: {0} left out to start {1:HH}:00 on time", drop.Track.Display, nextHour));
            return;
        }
    }

    /// <summary>
    /// Any time: when the first planned item of a later hour would start more than <see cref="Early"/> before its hour
    /// (the playlist ran short, or ran ahead, e.g. after items were removed), the gap is filled with songs from the
    /// template of the hour it falls in (optional, so a late hour can still drop them), and that hour starts on time.
    /// </summary>
    void KeepEarlyHours(IReadOnlyList<PlaylistRow> rows, DateTime now, DateTime? anchored)
    {
        var first = rows.FirstOrDefault(r => !r.IsPlayed && !r.IsPlaying && !r.IsPreview && ReadTag(r.Track.Tag) is { } t && t.Hour > Hour(now));
        if (first == null || first.StartAt is not { } start || ReadTag(first.Track.Tag) is not { } tag) return;
        if (tag.Hour == anchored) return; // a break that ends on that hour: it starts the hour on time
        var gap = tag.Hour - start;
        if (gap <= Early || _filled.TryGetValue(tag.Hour, out var until) && now < until) return;
        _filled[tag.Hour] = now.AddSeconds(30); // until the start times show the filled gap
        var before = first.Id;
        var hour = Hour(start);
        _busy = Task.Run(() => Fill(hour, before, start, gap));
    }

    readonly Dictionary<DateTime, DateTime> _filled = new();

    /// <summary>
    /// A break (or event) still to play that ends at its time on the hour, or just before it (as the time signal
    /// starts): the hour it ends, and its rows in the playlist (first and last index).
    /// </summary>
    internal static (DateTime Hour, int First, int Last)? HourEndingBreak(IReadOnlyList<PlaylistRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.IsPlayed || r.IsPreview || !r.Track.Backtime || r.Track.StartBy is not { } by) continue;
            int last = i;
            double length = r.Track.PlayLength;
            while (last + 1 < rows.Count && rows[last + 1].Track.Event == r.Track.Event && rows[last + 1].Track.StartBy == null)
                length += rows[++last].Track.PlayLength;
            var ends = by.ToLocalTime().AddSeconds(length);
            var hour = Hour(ends.AddSeconds(30));
            if ((hour - ends).TotalSeconds is >= -5 and <= 20) return (hour, i, last);
            i = last;
        }
        return null;
    }

    /// <summary>
    /// Songs of an earlier hour left after a break that ends on the hour (moved there because they didn't fit before
    /// it): they would play past the hour, so they go.
    /// </summary>
    void DropAfterBreak(IReadOnlyList<PlaylistRow> rows, (DateTime Hour, int First, int Last) anchor)
    {
        for (int i = anchor.Last + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (ReadTag(r.Track.Tag) is not { } t || t.Hour >= anchor.Hour) break; // the next hour starts here
            if (r.IsPlaying || !Droppable(r)) continue;
            _engine.Remove(r.Id);
            if (Store is { } store) { var hour = t.Hour; var path = r.Track.Path; _ = Task.Run(() => { try { store.KeepTime(hour, [], path); } catch { /* the plan stays as it was */ } }); }
            Say(L.F("Daily playlist: {0} left out, after the break that ends at {1:HH}:00", r.Track.Display, anchor.Hour));
            return; // one at a time: the rows change
        }
    }

    /// <summary>Songs from the template of <paramref name="hour"/> for <paramref name="gap"/>, put in before an item.</summary>
    void Fill(DateTime hour, Guid before, DateTime at, TimeSpan gap)
    {
        try
        {
            if (_library.Db is not { } db || _schedule() is not { } data || data.TemplateAt(hour) is not { } template) return;
            var lines = template.Lines.Where(l => l.TrackKind is null or TrackKind.Music).ToList();
            if (lines.Count == 0) return;
            var generator = new PlaylistGenerator(db, data, Options);
            generator.Remember(at, new PlanStore(db).Items(hour.AddHours(-3), hour.AddHours(3)));
            var items = new List<PlanItem>();
            double need = gap.TotalSeconds - Early.TotalSeconds / 2, got = 0;
            var avoid = new HashSet<string>();
            for (int i = 0; got < need && items.Count < 20; i++)
            {
                var line = lines[i % lines.Count];
                if (generator.Another(line, lines[(i + 1) % lines.Count], at.AddSeconds(got), avoid, template) is not { } item) break;
                avoid.Add(item.Path);
                items.Add(item);
                got += item.Seconds;
            }
            if (items.Count == 0) return;
            _engine.AddItemsBefore(items.Select(i => new ScheduledItem(i.Path, i.Kind, Tag: Tag(hour, true))).ToList(), before);
            // The plan shows what really plays (and the next pick knows these were taken).
            new PlanStore(db).KeepTime(hour, items);
            Say(items.Count == 1
                ? L.F("Daily playlist: {0} added, the hour ran short", items[0].Display)
                : L.F("Daily playlist: {0} songs added to fill {1:mm\\:ss} before {2:HH}:00", items.Count, gap, hour.AddHours(1)));
        }
        catch (Exception ex) { Say(L.F("Daily playlist: {0}", ex.Message)); }
    }

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}

/// <summary>Makes the hours of the daily playlists that are missing or out of date (for the playout and the manager).</summary>
static class PlanMaker
{
    /// <summary>
    /// Hours from <paramref name="from"/> to <paramref name="to"/>: those not made, and those whose template changed
    /// that start after <paramref name="changedFrom"/> and weren't edited by hand. Returns how many were made.
    /// </summary>
    public static int Ensure(PlanStore store, ScheduleData data, PlaylistGenerator.Options options, DateTime from, DateTime to,
        DateTime changedFrom, out List<string> problems)
    {
        problems = new();
        var made = store.Hours(from, to);
        var todo = new List<DateTime>();
        for (var h = from; h < to; h = h.AddHours(1))
        {
            if (!made.TryGetValue(h, out var hour)) todo.Add(h);
            // Changed templates: not near (it may be in the playlist already), unless it had none (nothing to be in it).
            else if (!hour.Edited && (h >= changedFrom || hour.TemplateId == null) && hour.Signature != PlaylistGenerator.Signature(data.TemplateAt(h))) todo.Add(h);
        }
        if (todo.Count == 0) return 0;
        return Make(store, data, options, todo, problems);
    }

    /// <summary>Makes these hours (in order), keeping songs apart from what played and what is planned around them.</summary>
    public static int Make(PlanStore store, ScheduleData data, PlaylistGenerator.Options options, IReadOnlyList<DateTime> hours,
        List<string> problems, bool evenIfEdited = false)
    {
        var generator = new PlaylistGenerator(store.Db, data, options);
        var window = new[] { options.SongGap, options.ArtistGap }
            .Concat(data.Templates.SelectMany(t => new[] { TimeSpan.FromHours(t.ArtistHours), TimeSpan.FromHours(t.AlbumHours) })).Max();
        var first = hours.Min();
        var around = store.Items(first - window - TimeSpan.FromHours(1), hours.Max().AddHours(1) + window);
        foreach (var h in hours) around.Remove(h);
        generator.Remember(first, around);
        int count = 0;
        foreach (var h in hours.Order())
        {
            var (hour, items, trouble) = generator.Generate(h);
            if (!store.Save(hour, items, unlessEdited: !evenIfEdited)) continue;
            count++;
            foreach (var t in trouble) problems.Add(L.F("Daily playlist {0:dd/MM HH}:00: nothing to play from {1}", h, t));
        }
        return count;
    }
}

/// <summary>
/// The playout's copy of the next hours of the daily playlists (plan-cache.json), for when the database can't be
/// reached: the station keeps playing what was planned.
/// </summary>
static class PlanCache
{
    static string File => Path.Combine(AppSettings.DataFolder, "plan-cache.json");

    public static void Save(PlanStore store, DateTime from, DateTime to)
    {
        var items = store.Items(from, to).ToDictionary(kv => PlanStore.HourKey(kv.Key), kv => kv.Value);
        SafeFile.WriteAllText(File, JsonSerializer.Serialize(items));
    }

    public static List<PlanItem>? Items(DateTime hour)
    {
        var all = SafeFile.Read(File, json => JsonSerializer.Deserialize<Dictionary<string, List<PlanItem>>>(json));
        return all?.GetValueOrDefault(PlanStore.HourKey(hour));
    }
}
