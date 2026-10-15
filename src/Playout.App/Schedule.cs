using System.Globalization;
using System.Text.Json;
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
        foreach (var e in data.Events) e.Times ??= new();
        foreach (var b in data.Breaks) b.Times ??= new();
        foreach (var s in data.Spots) s.Breaks ??= new();
        return data;
    }

    public void Save()
    {
        try { SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { AppLog.Write($"Schedule not saved: {ex.Message}"); }
    }

    public ScheduleData Clone() => JsonSerializer.Deserialize<ScheduleData>(JsonSerializer.Serialize(this, Json))!;
}

/// <summary>What events and ad breaks have in common: when they happen and how they start.</summary>
public abstract class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>"HH:mm" or "HH:mm:ss"; "*:mm" is every hour at that minute.</summary>
    public List<string> Times { get; set; } = new();
    /// <summary>Days of the week it runs (DayOfWeek: 0 = Sunday); empty = every day.</summary>
    public List<DayOfWeek> Days { get; set; } = new();
    /// <summary>Hard: starts on time, fading out what is on air. Soft: waits for the item on air to finish.</summary>
    public bool Hard { get; set; }

    public bool RunsOn(DayOfWeek day) => Days.Count == 0 || Days.Contains(day);

    /// <summary>Its times on a day (local), in order; times that can't be read are left out.</summary>
    public IEnumerable<DateTime> TimesOn(DateTime date)
    {
        if (!RunsOn(date.DayOfWeek)) yield break;
        var list = new List<DateTime>();
        foreach (var text in Times)
        {
            if (ScheduleTimes.Parse(text) is not { } t) continue;
            if (t.EveryHour)
                for (int h = 0; h < 24; h++) list.Add(date.Date.AddHours(h) + t.Time);
            else list.Add(date.Date + t.Time);
        }
        foreach (var at in list.Distinct().Order()) yield return at;
    }
}

/// <summary>Plays a file, the newest file in a folder (news bulletins) or an internet stream at set times.</summary>
public sealed class ScheduledEvent : ScheduleEntry
{
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

    public bool PlaysIn(AdBreak adBreak, DateTime at) =>
        Enabled && !string.IsNullOrWhiteSpace(Path)
        && (From == null || at.Date >= From.Value.Date) && (To == null || at.Date <= To.Value.Date)
        && (Days.Count == 0 || Days.Contains(at.DayOfWeek))
        && (Breaks.Count == 0 || Breaks.Contains(adBreak.Id));
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

/// <summary>
/// Starts events and ad breaks on time. Runs on its own timer (not the UI's), so a busy window never delays them.
/// An event that was due while the app wasn't running is started if it is at most <see cref="Grace"/> late
/// (a quick restart after a crash); older ones are skipped.
/// </summary>
sealed class Scheduler : IDisposable
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
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
            return old == null || !old.Times.SequenceEqual(entry.Times) || !old.Days.SequenceEqual(entry.Days) || old.Enabled != entry.Enabled;
        }
    }

    /// <summary>A copy of the schedule, for editing.</summary>
    public ScheduleData Current
    {
        get { lock (_lock) return _data.Clone(); }
    }

    static IEnumerable<ScheduleEntry> Entries(ScheduleData d) => d.Events.Cast<ScheduleEntry>().Concat(d.Breaks);

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
                // Today's and (just after midnight) yesterday's times that are due and not started yet: the latest one.
                var at = entry.TimesOn(now.Date.AddDays(-1)).Concat(entry.TimesOn(now.Date))
                    .Where(t => t <= now && t > last).DefaultIfEmpty(DateTime.MinValue).Max();
                if (at == DateTime.MinValue) continue;
                _data.LastStarted[entry.Id] = at;
                if (now - at > Grace) Say($"{Label(entry, at)}: missed (it was due {(now - at).TotalMinutes:0} minutes ago)");
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
            _engine.InsertEvent(Label(entry, at), items, entry.Hard);
        }
        catch (Exception ex)
        {
            Say($"{Label(entry, at)}: {ex.Message}");
        }
    }

    static string Label(ScheduleEntry entry, DateTime at) =>
        $"{(string.IsNullOrWhiteSpace(entry.Name) ? entry is AdBreak ? "Ad break" : "Event" : entry.Name.Trim())} {at:HH:mm}";

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
                    if (newest == null) { if (!quiet) Say($"{label}: no audio files in {e.Path}"); }
                    else if (e.MaxAgeMinutes > 0 && DateTime.UtcNow - newest.LastWriteTimeUtc > TimeSpan.FromMinutes(e.MaxAgeMinutes))
                    {
                        if (!quiet) Say($"{label}: skipped, the newest file ({newest.Name}) is older than {e.MaxAgeMinutes} minutes");
                    }
                    else items.Add(new ScheduledItem(newest.FullName, TrackKind.Programme));
                }
                else if (File.Exists(e.Path)) items.Add(new ScheduledItem(e.Path, TrackKind.Programme));
                else if (!quiet) Say($"{label}: {(string.IsNullOrWhiteSpace(e.Path) ? "nothing chosen to play" : $"not found: {e.Path}")}");
                break;

            case AdBreak b:
                List<AdSpot> spots;
                lock (_lock) spots = _data.Spots.Where(s => s.PlaysIn(b, at)).ToList();
                foreach (var spot in spots)
                {
                    if (File.Exists(spot.Path))
                        items.Add(new ScheduledItem(spot.Path, TrackKind.Commercial, Advertiser: string.IsNullOrWhiteSpace(spot.Advertiser) ? null : spot.Advertiser.Trim()));
                    else if (!quiet) Say($"{label}: advert not found, left out: {spot.Path}");
                }
                if (items.Count == 0)
                {
                    if (!quiet) Say($"{label}: no adverts booked, skipped");
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
