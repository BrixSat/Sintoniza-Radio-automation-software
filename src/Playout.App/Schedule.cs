using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Fixed-time events, ad breaks and the adverts booked into them; kept in schedule.json next to the settings.
/// </summary>
public sealed class ScheduleData
{
    public List<ScheduledEvent> Events { get; set; } = new();
    public List<AdBreak> Breaks { get; set; } = new();
    public List<AdSpot> Spots { get; set; } = new();
    /// <summary>The last time each event and break was started (local time), so a restart doesn't start it again.</summary>
    public Dictionary<Guid, DateTime> LastStarted { get; set; } = new();

    static string FilePath => Path.Combine(AppSettings.DataFolder, "schedule.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static ScheduleData Load()
    {
        var data = SafeFile.Read(FilePath, json => JsonSerializer.Deserialize<ScheduleData>(json)) ?? new();
        data.Events ??= new();
        data.Breaks ??= new();
        data.Spots ??= new();
        data.LastStarted ??= new();
        data.Normalize();
        return data;
    }

    public void Save()
    {
        try { SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { AppLog.Write($"Schedule not saved: {ex.Message}"); }
    }

    public ScheduleData Clone()
    {
        var copy = JsonSerializer.Deserialize<ScheduleData>(JsonSerializer.Serialize(this, Json))!;
        copy.Normalize();
        return copy;
    }

    /// <summary>Lists that are missing made empty, and schedules saved before the calendar brought into it.</summary>
    public void Normalize()
    {
        Events ??= new();
        Breaks ??= new();
        Spots ??= new();
        LastStarted ??= new();
        foreach (var e in Events.Cast<ScheduleEntry>().Concat(Breaks)) e.Normalize();
        foreach (var s in Spots)
        {
            s.Breaks ??= new();
            s.Days ??= new();
            s.Placements ??= new();
        }
    }
}

/// <summary>What events and ad breaks have in common: when they happen and how they start.</summary>
public abstract class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>When it runs: days of the week and times, as placed in the calendar.</summary>
    public List<WeekTime> Slots { get; set; } = new();

    /// <summary>
    /// Schedules saved before the calendar, and a quick way to set one up: times ("HH:mm", "HH:mm:ss"; "*:mm" is
    /// every hour) on <see cref="Days"/> (none = every day). <see cref="Normalize"/> turns them into slots.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Times { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DayOfWeek>? Days { get; set; }
    /// <summary>
    /// Soft: after the item on air (at most <see cref="MaxDelayMinutes"/> late). Hard: on time, fading out what is on
    /// air. EndAt: its times are when it ends; the playlist before it is adjusted (backtimed) so it does.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventStart Start { get; set; }

    /// <summary>Schedules saved before <see cref="Start"/>: true read as Hard.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hard { get => false; set { if (value) Start = EventStart.Hard; } }

    /// <summary>Soft: if the item on air hasn't ended this many minutes after the time, it fades out (0 = no limit).</summary>
    public double MaxDelayMinutes { get; set; }

    /// <summary>What the operator may do with its items in the playlist.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemLock Lock { get; set; }

    /// <summary>How it starts, for the engine; <paramref name="at"/> is the time it is for (local).</summary>
    public EventTiming Timing(DateTime at) => Start switch
    {
        EventStart.Hard => EventTiming.Hard,
        EventStart.EndAt => new EventTiming(EventStart.EndAt, EndsAt: at.ToUniversalTime()),
        _ => new EventTiming(EventStart.Soft, MaxDelayMinutes > 0 ? TimeSpan.FromMinutes(MaxDelayMinutes) : null),
    };

    /// <summary>Turns <see cref="Times"/> and <see cref="Days"/> into slots.</summary>
    public void Normalize()
    {
        Slots ??= new();
        if (Times != null)
        {
            var days = Days is { Count: > 0 } d ? d : Enum.GetValues<DayOfWeek>().ToList();
            foreach (var text in Times)
            {
                if (ScheduleTimes.Parse(text) is not { } t) continue;
                var times = t.EveryHour ? Enumerable.Range(0, 24).Select(h => TimeSpan.FromHours(h) + t.Time) : [t.Time];
                foreach (var day in days)
                    foreach (var time in times)
                        Set(day, time, true);
            }
        }
        Times = null;
        Days = null;
    }

    /// <summary>Whether it runs on that day at that time.</summary>
    public bool Has(DayOfWeek day, TimeSpan time) => Slots.Any(s => s.Day == day && s.At == time);

    public void Set(DayOfWeek day, TimeSpan time, bool on)
    {
        Slots.RemoveAll(s => s.Day == day && s.At == time);
        if (on) Slots.Add(new WeekTime { Day = day, Time = WeekTime.Text(time) });
    }

    /// <summary>Its times on a day (local), in order.</summary>
    public IEnumerable<DateTime> TimesOn(DateTime date) =>
        Slots.Where(s => s.Day == date.DayOfWeek).Select(s => date.Date + s.At).Distinct().Order();
}

/// <summary>A day of the week and a time of day.</summary>
public sealed class WeekTime
{
    public DayOfWeek Day { get; set; }
    /// <summary>"HH:mm:ss".</summary>
    public string Time { get; set; } = "00:00:00";

    [JsonIgnore]
    public TimeSpan At => ScheduleTimes.Parse(Time)?.Time ?? TimeSpan.Zero;

    public static string Text(TimeSpan time) => time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

/// <summary>Where an advert is placed in the calendar: every week on a day, or on one date; at a break's time.</summary>
public sealed class AdPlacement
{
    /// <summary>Every week on this day (within the advert's dates)…</summary>
    public DayOfWeek? Day { get; set; }
    /// <summary>…or only on this date.</summary>
    public DateTime? Date { get; set; }
    /// <summary>"HH:mm:ss": the break at this time.</summary>
    public string Time { get; set; } = "00:00:00";

    [JsonIgnore]
    public TimeSpan At => ScheduleTimes.Parse(Time)?.Time ?? TimeSpan.Zero;

    public bool On(DateTime at) => At == at.TimeOfDay && (Date is { } d ? d.Date == at.Date : Day == at.DayOfWeek);
}

/// <summary>Plays a file, the newest file in a folder (news bulletins) or an internet stream at set times.</summary>
public sealed class ScheduledEvent : ScheduleEntry
{
    public ScheduledEvent() => Lock = ItemLock.Movable;

    /// <summary>A file, a folder (its newest audio file) or an http(s) stream address.</summary>
    public string Path { get; set; } = "";
    /// <summary>A stream plays this long (0 = until Stop or Next).</summary>
    public double StreamMinutes { get; set; }
    /// <summary>A folder's newest file is only played if it is at most this old (0 = any age): an old bulletin isn't news.</summary>
    public int MaxAgeMinutes { get; set; }
}

/// <summary>An ad break: the adverts booked into it that day, between an optional opening and closing jingle.</summary>
public sealed class AdBreak : ScheduleEntry
{
    public AdBreak() => Lock = ItemLock.Locked;

    public string Intro { get; set; } = "";
    public string Outro { get; set; } = "";
}

/// <summary>An advert, booked into ad breaks between two dates.</summary>
public sealed class AdSpot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    public string Advertiser { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>First and last day it plays (inclusive); null = no limit.</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<DayOfWeek> Days { get; set; } = new();
    /// <summary>Ad breaks it plays in; empty = every break.</summary>
    public List<Guid> Breaks { get; set; } = new();
    /// <summary>
    /// Where it was placed in the calendar. With any, it plays only there (within its dates), and
    /// <see cref="Days"/> and <see cref="Breaks"/> don't count.
    /// </summary>
    public List<AdPlacement> Placements { get; set; } = new();

    public bool PlaysIn(AdBreak adBreak, DateTime at) =>
        Enabled && !string.IsNullOrWhiteSpace(Path)
        && (From == null || at.Date >= From.Value.Date) && (To == null || at.Date <= To.Value.Date)
        && (Placements.Count > 0
            ? Placements.Any(p => p.On(at))
            : (Days.Count == 0 || Days.Contains(at.DayOfWeek)) && (Breaks.Count == 0 || Breaks.Contains(adBreak.Id)));
}

public static class ScheduleTimes
{
    /// <summary>"07:30", "7:30:15", "*:30" (every hour at :30); null if it isn't a time.</summary>
    public static (TimeSpan Time, bool EveryHour)? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        bool everyHour = text.StartsWith('*');
        if (everyHour) text = "0" + text[1..];
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3) return null;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        if (numbers[0] > 23 || numbers[1] > 59 || numbers[2] > 59) return null;
        return (new TimeSpan(numbers[0], numbers[1], numbers[2]), everyHour);
    }

    /// <summary>Times typed as a list ("07:30, 12:00 *:45"); returns the ones that can't be read.</summary>
    public static (List<string> Times, List<string> Bad) Split(string? text)
    {
        var times = new List<string>();
        var bad = new List<string>();
        foreach (var part in (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            (Parse(part) != null ? times : bad).Add(part);
        return (times, bad);
    }

    public static string Join(IEnumerable<string> times) => string.Join(", ", times);
}

/// <summary>An event or break the schedule will put in the playlist (see <see cref="Scheduler.Upcoming"/>).</summary>
/// <param name="Start">When it is expected to start (an EndAt one: its length before <paramref name="Due"/>).</param>
/// <param name="Due">Its time in the schedule.</param>
/// <param name="MaxDelayMinutes">Soft: at most this late (0 = it waits for the song on air).</param>
public sealed record UpcomingEvent(Guid EntryId, string Label, string What, DateTime Start, DateTime Due, EventStart Timing, double Seconds, bool IsBreak,
    double MaxDelayMinutes = 0)
{
    /// <summary>How it starts, for the playlist and the countdown: "hard start at 12:20:00", "can wait 5 min"…</summary>
    public string How => Timing switch
    {
        EventStart.Hard => L.F("hard start at {0:HH:mm:ss}", Due),
        EventStart.EndAt => L.F("ends at {0:HH:mm:ss}", Due),
        _ => MaxDelayMinutes > 0 ? L.F("from {0:HH:mm}, can wait {1:0} min", Due, MaxDelayMinutes) : L.F("from {0:HH:mm}, waits for the song", Due),
    };
}

/// <summary>
/// Starts events and ad breaks on time. Runs on its own timer (not the UI's), so a busy window never delays them.
/// An event that was due while the app wasn't running is started if it is at most <see cref="Grace"/> late
/// (a quick restart after a crash); older ones are skipped.
/// </summary>
sealed class Scheduler : IDisposable
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
    /// <summary>
    /// What ends at a time goes into the playlist this long before, so there is time to adjust the songs before it.
    /// Its files are chosen then (a folder's newest file then).
    /// </summary>
    public static readonly TimeSpan EndAtLead = TimeSpan.FromMinutes(15);
    /// <summary>Files of events coming up this soon are measured for loudness levelling ahead of time.</summary>
    static readonly TimeSpan Prepare = TimeSpan.FromMinutes(10);

    readonly PlayoutEngine _engine;
    readonly Timer _timer;
    readonly object _lock = new();
    ScheduleData _data;
    DateTime _prepared = DateTime.MinValue;

    /// <summary>A line for the status bar and the log (raised on the timer thread).</summary>
    public event Action<string>? Message;

    public Scheduler(PlayoutEngine engine, ScheduleData data)
    {
        _engine = engine;
        _data = data;
        _timer = new Timer(_ => Check(DateTime.Now), null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));
    }

    /// <summary>The schedule as edited (a copy is kept). Entries that were due before now don't start because of the edit.</summary>
    public void Update(ScheduleData data)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var copy = data.Clone();
            foreach (var entry in Entries(copy))
                if (!_data.LastStarted.ContainsKey(entry.Id) || Changed(entry)) copy.LastStarted[entry.Id] = now;
                else copy.LastStarted[entry.Id] = _data.LastStarted[entry.Id];
            foreach (var id in copy.LastStarted.Keys.Where(id => Entries(copy).All(e => e.Id != id)).ToList()) copy.LastStarted.Remove(id);
            _data = copy;
            _data.Save();
            _prepared = DateTime.MinValue;
        }

        bool Changed(ScheduleEntry entry)
        {
            var old = Entries(_data).FirstOrDefault(e => e.Id == entry.Id);
            return old == null || old.Enabled != entry.Enabled
                   || !old.Slots.Select(x => (x.Day, x.At)).ToHashSet().SetEquals(entry.Slots.Select(x => (x.Day, x.At)));
        }
    }

    /// <summary>A copy of the schedule, for editing.</summary>
    public ScheduleData Current
    {
        get { lock (_lock) return _data.Clone(); }
    }

    static IEnumerable<ScheduleEntry> Entries(ScheduleData d) => d.Events.Cast<ScheduleEntry>().Concat(d.Breaks);

    /// <summary>
    /// What the schedule will put in the playlist from now until <paramref name="until"/>, not there yet: each event
    /// and break that has something to play, when it is due, what it plays and how long that is. For the playlist
    /// to show ahead. Reads folders and files, so not on the UI thread.
    /// </summary>
    public List<UpcomingEvent> Upcoming(DateTime now, DateTime until)
    {
        List<(ScheduleEntry Entry, DateTime At)> due;
        lock (_lock)
        {
            due = Entries(_data).Where(e => e.Enabled)
                .SelectMany(e => e.TimesOn(now.Date).Concat(e.TimesOn(now.Date.AddDays(1))).Select(at => (Entry: e, At: at)))
                .Where(x => x.At > now && x.At <= until && x.At > _data.LastStarted.GetValueOrDefault(x.Entry.Id, DateTime.MinValue)
                            && !(x.Entry.Start == EventStart.EndAt && x.At - EndAtLead <= now)) // those are in the playlist already
                .OrderBy(x => x.At).ToList();
        }
        var list = new List<UpcomingEvent>();
        foreach (var (entry, at) in due)
        {
            var items = Items(entry, at, quiet: true);
            if (items.Count == 0) continue; // a break with no adverts, a missing file: nothing will play
            double seconds = items.Sum(i => i.StreamName != null ? i.StreamSeconds : Length(i.Path));
            string what = entry switch
            {
                AdBreak => items.Count(i => i.Kind == TrackKind.Commercial) is var n && n == 1 ? L.T("1 advert") : L.F("{0} adverts", n),
                _ => items[0].StreamName != null ? L.T("stream") : System.IO.Path.GetFileNameWithoutExtension(items[0].Path),
            };
            var start = entry.Start == EventStart.EndAt ? at.AddSeconds(-seconds) : at;
            list.Add(new UpcomingEvent(entry.Id, Label(entry, at), what, start, at, entry.Start, seconds, entry is AdBreak, entry.MaxDelayMinutes));
        }
        return list;
    }

    readonly Dictionary<string, double> _lengths = new();

    double Length(string path)
    {
        lock (_lengths)
        {
            if (_lengths.TryGetValue(path, out double s)) return s;
            s = File.Exists(path) ? AudioProbe.Duration(path) ?? 0 : 0;
            return _lengths[path] = s;
        }
    }

    /// <summary>The next event or break due after <paramref name="now"/> (within a day), for the station panel.</summary>
    public (DateTime At, string Name)? Next(DateTime now)
    {
        lock (_lock)
        {
            (DateTime At, string Name)? next = null;
            foreach (var entry in Entries(_data).Where(e => e.Enabled))
                foreach (var at in entry.TimesOn(now.Date).Concat(entry.TimesOn(now.Date.AddDays(1))))
                {
                    if (at <= now) continue;
                    if (next == null || at < next.Value.At) next = (at, entry.Name);
                    break;
                }
            return next;
        }
    }

    int _checking;

    internal void Check(DateTime now)
    {
        // Timer callbacks can overlap when one takes long (a slow network folder): one at a time.
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try { CheckDue(now); }
        finally { Volatile.Write(ref _checking, 0); }
    }

    void CheckDue(DateTime now)
    {
        var due = new List<(ScheduleEntry Entry, DateTime At)>();
        lock (_lock)
        {
            foreach (var entry in Entries(_data).Where(e => e.Enabled))
            {
                var last = _data.LastStarted.GetValueOrDefault(entry.Id, DateTime.MinValue);
                // Today's and (around midnight) yesterday's and tomorrow's times that are due and not started yet: the
                // latest one. What ends at a time is due EndAtLead before it.
                var lead = entry.Start == EventStart.EndAt ? EndAtLead : TimeSpan.Zero;
                var at = entry.TimesOn(now.Date.AddDays(-1)).Concat(entry.TimesOn(now.Date)).Concat(entry.TimesOn(now.Date.AddDays(1)))
                    .Where(t => t - lead <= now && t > last).DefaultIfEmpty(DateTime.MinValue).Max();
                if (at == DateTime.MinValue) continue;
                _data.LastStarted[entry.Id] = at;
                bool missed = entry.Start == EventStart.EndAt ? now > at : now - at > Grace;
                if (missed) Say(L.F("{0}: missed (it was due {1:0} minutes ago)", Label(entry, at), (now - at).TotalMinutes));
                else due.Add((entry, at));
            }
            if (now - _prepared > TimeSpan.FromMinutes(1)) PrepareSoon(now);
            if (due.Count > 0) _data.Save();
        }
        // Breaks first when several are due at once: they are booked to the second, events can wait.
        foreach (var (entry, at) in due.OrderBy(d => d.At).ThenBy(d => d.Entry is AdBreak ? 0 : 1))
            Start(entry, at);
    }

    /// <summary>Measures (for loudness levelling) the files of what starts in the next few minutes.</summary>
    void PrepareSoon(DateTime now)
    {
        _prepared = now;
        var files = new List<string>();
        foreach (var entry in Entries(_data).Where(e => e.Enabled))
            foreach (var at in entry.TimesOn(now.Date).Concat(entry.TimesOn(now.Date.AddDays(1))).Where(t => t > now && t - now <= Prepare))
                files.AddRange(Items(entry, at, quiet: true).Where(i => i.StreamName == null).Select(i => i.Path));
        _engine.MeasureLoudness(files.Distinct());
    }

    void Start(ScheduleEntry entry, DateTime at)
    {
        try
        {
            var items = Items(entry, at, quiet: false);
            if (items.Count == 0) return;
            _engine.InsertEvent(Label(entry, at), items.Select(i => i with { Lock = entry.Lock }).ToList(), entry.Timing(at));
        }
        catch (Exception ex)
        {
            Say(L.F("{0}: {1}", Label(entry, at), ex.Message));
        }
    }

    static string Label(ScheduleEntry entry, DateTime at) =>
        L.F("{0} {1:HH:mm}", (string.IsNullOrWhiteSpace(entry.Name) ? entry is AdBreak ? L.T("Ad break") : L.T("Event") : entry.Name.Trim()), at);

    /// <summary>What an event or break plays at <paramref name="at"/>; quiet: no messages (looking ahead).</summary>
    List<ScheduledItem> Items(ScheduleEntry entry, DateTime at, bool quiet)
    {
        var items = new List<ScheduledItem>();
        string label = Label(entry, at);
        switch (entry)
        {
            case ScheduledEvent e:
                if (PlayoutEngine.IsUrl(e.Path))
                    items.Add(new ScheduledItem(e.Path.Trim(), StreamName: string.IsNullOrWhiteSpace(e.Name) ? e.Path.Trim() : e.Name.Trim(),
                        StreamSeconds: Math.Max(0, e.StreamMinutes) * 60));
                else if (Directory.Exists(e.Path))
                {
                    var newest = AudioFiles.InFolder(e.Path).Select(f => new FileInfo(f)).MaxBy(f => f.LastWriteTimeUtc);
                    if (newest == null) { if (!quiet) Say(L.F("{0}: no audio files in {1}", label, e.Path)); }
                    else if (e.MaxAgeMinutes > 0 && DateTime.UtcNow - newest.LastWriteTimeUtc > TimeSpan.FromMinutes(e.MaxAgeMinutes))
                    {
                        if (!quiet) Say(L.F("{0}: skipped, the newest file ({1}) is older than {2} minutes", label, newest.Name, e.MaxAgeMinutes));
                    }
                    else items.Add(new ScheduledItem(newest.FullName, TrackKind.Programme));
                }
                else if (File.Exists(e.Path)) items.Add(new ScheduledItem(e.Path, TrackKind.Programme));
                else if (!quiet) Say(string.IsNullOrWhiteSpace(e.Path) ? L.F("{0}: nothing chosen to play", label) : L.F("{0}: not found: {1}", label, e.Path));
                break;

            case AdBreak b:
                List<AdSpot> spots;
                lock (_lock) spots = _data.Spots.Where(s => s.PlaysIn(b, at)).ToList();
                foreach (var spot in spots)
                {
                    if (File.Exists(spot.Path))
                        items.Add(new ScheduledItem(spot.Path, TrackKind.Commercial, Advertiser: string.IsNullOrWhiteSpace(spot.Advertiser) ? null : spot.Advertiser.Trim()));
                    else if (!quiet) Say(L.F("{0}: advert not found, left out: {1}", label, spot.Path));
                }
                if (items.Count == 0)
                {
                    if (!quiet) Say(L.F("{0}: no adverts booked, skipped", label));
                    break;
                }
                if (File.Exists(b.Intro)) items.Insert(0, new ScheduledItem(b.Intro, TrackKind.Jingle));
                if (File.Exists(b.Outro)) items.Add(new ScheduledItem(b.Outro, TrackKind.Jingle));
                break;
        }
        return items;
    }

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");

    public void Dispose() => _timer.Dispose();
}
