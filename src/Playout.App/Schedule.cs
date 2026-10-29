using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Fixed-time events, ad breaks and the clients' campaigns booked into them; kept in schedule.json next to the settings.
/// </summary>
public sealed class ScheduleData
{
    public List<ScheduledEvent> Events { get; set; } = new();
    public List<AdBreak> Breaks { get; set; } = new();
    public List<AdClient> Clients { get; set; } = new();
    /// <summary>Hour templates: the daily playlists are made from them (see <see cref="PlaylistGenerator"/>).</summary>
    public List<PlaylistTemplate> Templates { get; set; } = new();
    /// <summary>Adverts as saved before clients and campaigns: <see cref="Normalize"/> turns them into those.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AdSpot>? Spots { get; set; }
    /// <summary>The last time each event and break was started (local time), so a restart doesn't start it again.</summary>
    public Dictionary<Guid, DateTime> LastStarted { get; set; } = new();

    /// <summary>Where the schedule was kept before it moved into the database: imported from there once.</summary>
    public static string LegacyFile => Path.Combine(AppSettings.DataFolder, "schedule.json");
    static string LegacyStateFile => Path.Combine(AppSettings.DataFolder, "schedule-state.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The schedule as kept in files before the database (schedule.json and when each started); null if none.</summary>
    public static ScheduleData? LoadLegacy()
    {
        if (!File.Exists(LegacyFile)) return null;
        var data = SafeFile.Read(LegacyFile, json => JsonSerializer.Deserialize<ScheduleData>(json)) ?? new();
        if (SafeFile.Read(LegacyStateFile, json => JsonSerializer.Deserialize<Dictionary<Guid, DateTime>>(json)) is { } state)
            foreach (var (id, at) in state) data.LastStarted[id] = at;
        data.Normalize();
        return data;
    }

    /// <summary>After the import: the old files renamed, so it is clear they are no longer used.</summary>
    public static void RetireLegacy()
    {
        foreach (var file in new[] { LegacyFile, LegacyStateFile, LegacyFile + ".bak", LegacyStateFile + ".bak" })
            try { if (File.Exists(file)) File.Move(file, file + ".imported", true); }
            catch (Exception ex) { AppLog.Write($"{Path.GetFileName(file)} not renamed: {ex.Message}"); }
    }

    /// <summary>A file with the whole schedule and when each started (the playout's local copy); null if none.</summary>
    public static ScheduleData? ReadFile(string path)
    {
        var data = SafeFile.Read(path, json => JsonSerializer.Deserialize<ScheduleData>(json));
        data?.Normalize();
        return data;
    }

    public void WriteFile(string path)
    {
        try { SafeFile.WriteAllText(path, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { AppLog.Write($"{Path.GetFileName(path)} not saved: {ex.Message}"); }
    }

    public ScheduleData Clone()
    {
        var copy = JsonSerializer.Deserialize<ScheduleData>(JsonSerializer.Serialize(this, Json))!;
        copy.Normalize();
        return copy;
    }

    /// <summary>
    /// Lists that are missing made empty, and schedules saved before brought up to date: times into the calendar,
    /// opening and closing jingles into the break's running order, adverts into clients and campaigns.
    /// </summary>
    public void Normalize()
    {
        Events ??= new();
        Breaks ??= new();
        Clients ??= new();
        Templates ??= new();
        LastStarted ??= new();
        foreach (var e in Events.Cast<ScheduleEntry>().Concat(Breaks).Concat(Templates)) e.Normalize();
        foreach (var c in Clients)
        {
            c.Campaigns ??= new();
            foreach (var k in c.Campaigns)
            {
                k.Files ??= new();
                k.Placements ??= new();
                k.Section ??= "";
            }
        }
        if (Spots != null) FromSpots(Spots);
        Spots = null;
    }

    /// <summary>
    /// Adverts saved before clients and campaigns: a client for each advertiser, a campaign for each advert, in the
    /// first section of the breaks. One that played by days and breaks is placed in the calendar there instead.
    /// </summary>
    void FromSpots(List<AdSpot> spots)
    {
        foreach (var spot in spots)
        {
            if (spot == null) continue;
            string name = string.IsNullOrWhiteSpace(spot.Advertiser) ? Path.GetFileNameWithoutExtension(spot.Path ?? "") : spot.Advertiser.Trim();
            var client = Clients.FirstOrDefault(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            if (client == null) Clients.Add(client = new AdClient { Name = name });
            var placements = spot.Placements ?? new();
            if (placements.Count == 0)
            {
                var days = spot.Days ?? new();
                var breaks = spot.Breaks ?? new();
                foreach (var b in Breaks.Where(b => breaks.Count == 0 || breaks.Contains(b.Id)))
                    foreach (var slot in b.Slots.Where(s => days.Count == 0 || days.Contains(s.Day)))
                        if (!placements.Any(p => p.Day == slot.Day && p.At == slot.At))
                            placements.Add(new AdPlacement { Day = slot.Day, Time = slot.Time });
            }
            client.Campaigns.Add(new AdCampaign
            {
                Id = spot.Id,
                Name = Path.GetFileNameWithoutExtension(spot.Path ?? ""),
                Enabled = spot.Enabled,
                From = spot.From,
                To = spot.To,
                Files = string.IsNullOrWhiteSpace(spot.Path) ? new() : [spot.Path],
                Placements = placements,
            });
        }
    }

    /// <summary>The template for the hour that starts at <paramref name="hour"/> (null if none is placed there).</summary>
    public PlaylistTemplate? TemplateAt(DateTime hour) =>
        Templates.FirstOrDefault(t => t.Enabled && t.Lines.Count > 0 && t.Slots.Any(s => s.Day == hour.DayOfWeek && s.At.Hours == hour.Hour));

    /// <summary>The section names used in the breaks, in the order they first appear.</summary>
    public List<string> Sections() =>
        Breaks.SelectMany(b => b.Parts).Where(p => p.Kind == PartKind.Section && p.Section.Trim().Length > 0)
            .Select(p => p.Section.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// The adverts in a section of a break at <paramref name="at"/>, in the order they play: positions 1–5 first, then
    /// the automatic ones (the same client not twice in a row where it can be helped), then positions 6–10.
    /// </summary>
    public List<BookedAdvert> InSection(AdBreak b, string section, DateTime at)
    {
        var booked = new List<BookedAdvert>();
        foreach (var client in Clients.Where(c => c.Enabled))
            foreach (var k in client.Campaigns)
                if (k.PlaysAt(at) && b.SectionOf(k) is { } s && s.Equals(section.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    && k.FileFor(at) is { } file)
                    booked.Add(new BookedAdvert(client, k, file));

        var auto = booked.Where(a => a.Campaign.Position == 0).ToList();
        for (int i = 1; i < auto.Count; i++)
        {
            if (auto[i].Client != auto[i - 1].Client) continue;
            int j = auto.FindIndex(i + 1, a => a.Client != auto[i - 1].Client);
            if (j > 0) (auto[i], auto[j]) = (auto[j], auto[i]);
        }
        return booked.Where(a => a.Campaign.Position is >= 1 and <= 5).OrderBy(a => a.Campaign.Position)
            .Concat(auto)
            .Concat(booked.Where(a => a.Campaign.Position >= 6).OrderBy(a => a.Campaign.Position)).ToList();
    }

    /// <summary>All the adverts in a break at <paramref name="at"/>, section by section.</summary>
    public List<BookedAdvert> InBreak(AdBreak b, DateTime at) =>
        b.Parts.Where(p => p.Kind == PartKind.Section).Select(p => p.Section.Trim()).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(s => InSection(b, s, at)).ToList();
}

/// <summary>An advert going out in a break: whose, from which campaign, and the file (campaigns with several take turns).</summary>
public sealed record BookedAdvert(AdClient Client, AdCampaign Campaign, string File);

/// <summary>What events and ad breaks have in common: when they happen and how they start.</summary>
public abstract class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>When it runs: days of the week and times, as placed in the calendar.</summary>
    public List<WeekTime> Slots { get; set; } = new();
    /// <summary>Once, at these dates and times (local), besides the weekly slots: e.g. a stream put in a day's playlist.</summary>
    public List<DateTime> Dates { get; set; } = new();

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
    public virtual void Normalize()
    {
        Slots ??= new();
        Dates ??= new();
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
        Slots.Where(s => s.Day == date.DayOfWeek).Select(s => date.Date + s.At).Concat(Dates.Where(d => d.Date == date.Date)).Distinct().Order();
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

/// <summary>
/// An ad break: its running order of jingles and advert sections. A section plays the adverts booked into it that day;
/// a break with none in any section is skipped.
/// </summary>
public sealed class AdBreak : ScheduleEntry
{
    public AdBreak() => Lock = ItemLock.Locked;

    public List<BreakPart> Parts { get; set; } = new();

    /// <summary>Breaks saved before the running order: <see cref="Normalize"/> puts these jingles in it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Intro { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outro { get; set; }

    public override void Normalize()
    {
        base.Normalize();
        Parts ??= new();
        if (Parts.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(Intro)) Parts.Add(new BreakPart { Kind = PartKind.Jingle, Path = Intro });
            Parts.Add(new BreakPart { Kind = PartKind.Section, Section = DefaultSection });
            if (!string.IsNullOrWhiteSpace(Outro)) Parts.Add(new BreakPart { Kind = PartKind.Jingle, Path = Outro });
        }
        foreach (var p in Parts)
        {
            p.Path ??= "";
            p.Section ??= "";
        }
        Intro = Outro = null;
    }

    /// <summary>The section a new break has, and old adverts were put in.</summary>
    public static string DefaultSection => L.T("Adverts");

    public IEnumerable<string> SectionNames => Parts.Where(p => p.Kind == PartKind.Section).Select(p => p.Section.Trim());

    /// <summary>The section of this break a campaign's adverts go in (no section chosen: the first); null if it hasn't that one.</summary>
    public string? SectionOf(AdCampaign k)
    {
        var names = SectionNames.ToList();
        if (k.Section.Trim().Length == 0) return names.FirstOrDefault();
        return names.FirstOrDefault(n => n.Equals(k.Section.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }
}

public enum PartKind { Jingle, Section }

/// <summary>
/// An hour template: its lines in order (a song from a library category, a file from a folder), placed on the week
/// calendar by hour. The daily playlists are made from it ahead of time (<see cref="PlaylistGenerator"/>).
/// </summary>
public sealed class PlaylistTemplate : ScheduleEntry
{
    public List<TemplateLine> Lines { get; set; } = new();
    /// <summary>The same artist not again within this many hours (0 = the rest time in the playout's settings).</summary>
    public double ArtistHours { get; set; }
    /// <summary>Not two songs of the same album (and artist) within this many hours (0 = no album rule).</summary>
    public double AlbumHours { get; set; }
    /// <summary>At most this many slow songs (tempo 1–2) in a row (0 = no limit).</summary>
    public int MaxSlowInRow { get; set; }

    public override void Normalize()
    {
        base.Normalize();
        Lines ??= new();
        foreach (var l in Lines)
        {
            l.Folder ??= "";
            l.CategoryName ??= "";
            l.Kind ??= "music";
        }
    }
}

/// <summary>Where a template line's item comes from: a library category, a folder, or a take (put together on air).</summary>
public enum LineSource { Category, Folder, Take }

/// <summary>A line of an hour template: one item, from a library category or a folder.</summary>
public sealed class TemplateLine
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LineSource Source { get; set; }
    public long CategoryId { get; set; }
    /// <summary>The category's name and kind (music, jingle, commercial, other) when it was chosen, to show and to tag what it plays.</summary>
    public string CategoryName { get; set; } = "";
    public string Kind { get; set; } = "music";
    /// <summary>A folder line's folder; a take line's take (take:time, take:temperature, take:time+temperature).</summary>
    public string Folder { get; set; } = "";
    /// <summary>A library line's songs only with a tempo (1 slow … 5 fast) and energy (1 calm … 5 lively) in these ranges (null: any).</summary>
    public int? TempoMin { get; set; }
    public int? TempoMax { get; set; }
    public int? EnergyMin { get; set; }
    public int? EnergyMax { get; set; }

    /// <summary>Whether it asks for a tempo or energy.</summary>
    [JsonIgnore]
    public bool HasFilter => TempoMin != null || TempoMax != null || EnergyMin != null || EnergyMax != null;

    /// <summary>What the playlist calls what it plays: a jingle category's files are jingles wherever they are kept; a folder's go by the folder.</summary>
    [JsonIgnore]
    public TrackKind? TrackKind => Source == LineSource.Folder ? null : Source == LineSource.Take ? Playout.Engine.TrackKind.Jingle : Kind switch
    {
        "jingle" => Playout.Engine.TrackKind.Jingle,
        "commercial" => Playout.Engine.TrackKind.Commercial,
        "other" => Playout.Engine.TrackKind.Programme,
        _ => Playout.Engine.TrackKind.Music,
    };
}

/// <summary>A step in a break's running order: a jingle, or a section where adverts go.</summary>
public sealed class BreakPart
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PartKind Kind { get; set; }
    /// <summary>A jingle's file, or <see cref="CategoryPrefix"/> and a category's id: one of its files, chosen as the break goes on air.</summary>
    public string Path { get; set; } = "";
    /// <summary>
    /// A section's name ("Before the hour", "Promos"…): campaigns are booked into it by name. A part from a category:
    /// the category's name, as shown.
    /// </summary>
    public string Section { get; set; } = "";

    /// <summary>
    /// A part that takes a file from a library category (Jingles, Promos, Fillers… not Music) each time the break goes
    /// on air. Kept as a jingle whose file is "category:" and the category's id: a version without it leaves it out as
    /// a jingle it can't find.
    /// </summary>
    public const string CategoryPrefix = "category:";

    [JsonIgnore]
    public long? CategoryId => Kind == PartKind.Jingle && Path.StartsWith(CategoryPrefix, StringComparison.Ordinal)
        && long.TryParse(Path.AsSpan(CategoryPrefix.Length), out long id) ? id : null;

    public static BreakPart FromCategory(long categoryId, string name) => new() { Kind = PartKind.Jingle, Path = CategoryPrefix + categoryId, Section = name };
    /// <summary>A jingle that only plays when the section after it has adverts (a section opener).</summary>
    public bool OnlyWithAdverts { get; set; }
}

/// <summary>An advertiser, with its campaigns. Its name goes on the proof of broadcast.</summary>
public sealed class AdClient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<AdCampaign> Campaigns { get; set; } = new();
}

/// <summary>A client's campaign: its adverts, the section and place in the break they go in, and the breaks they are placed in.</summary>
public sealed class AdCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>First and last day it plays (inclusive); null = no limit.</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>Its audio files; with several they take turns.</summary>
    public List<string> Files { get; set; } = new();
    /// <summary>The section of the break it goes in ("" = the break's first).</summary>
    public string Section { get; set; } = "";
    /// <summary>0 = automatic; 1–5 = that place from the start of the section; 6–10 at its end (10 = last).</summary>
    public int Position { get; set; }
    /// <summary>The breaks it goes in, as placed in the calendar.</summary>
    public List<AdPlacement> Placements { get; set; } = new();

    public bool PlaysAt(DateTime at) =>
        Enabled && Files.Count > 0
        && (From == null || at.Date >= From.Value.Date) && (To == null || at.Date <= To.Value.Date)
        && Placements.Any(p => p.On(at));

    /// <summary>Which of its files plays at <paramref name="at"/>: they take turns, through the day and from one day to the next.</summary>
    public string? FileFor(DateTime at)
    {
        var files = Files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (files.Count == 0) return null;
        int earlier = Placements.Where(p => p.At < at.TimeOfDay && p.On(at.Date + p.At)).Select(p => p.At).Distinct().Count();
        int day = (at.Date - new DateTime(2000, 1, 1)).Days;
        return files[(day + earlier) % files.Count];
    }
}

/// <summary>An advert as saved before clients and campaigns (read, then turned into a campaign).</summary>
public sealed class AdSpot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "";
    public string Advertiser { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<DayOfWeek>? Days { get; set; }
    public List<Guid>? Breaks { get; set; }
    public List<AdPlacement>? Placements { get; set; }
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
/// <param name="Empty">A break with no adverts booked: shown, so it can be seen, but nothing will play.</param>
/// <param name="Items">What it plays, each item with its length: shown one by one in the playlist.</param>
public sealed record UpcomingEvent(Guid EntryId, string Label, string What, DateTime Start, DateTime Due, EventStart Timing, double Seconds, bool IsBreak,
    double MaxDelayMinutes = 0, bool Empty = false, IReadOnlyList<UpcomingItem>? Items = null)
{
    /// <summary>How it starts, for the playlist and the countdown: "hard start at 12:20:00", "can wait 5 min"…</summary>
    public string How => Timing switch
    {
        EventStart.Hard => L.F("hard start at {0:HH:mm:ss}", Due),
        EventStart.EndAt => L.F("ends at {0:HH:mm:ss}", Due),
        _ => MaxDelayMinutes > 0 ? L.F("from {0:HH:mm}, can wait {1:0} min", Due, MaxDelayMinutes) : L.F("from {0:HH:mm}, waits for the song", Due),
    };
}

/// <summary>An item of an event or break coming up: its file (or stream), type, advertiser and length.</summary>
public sealed record UpcomingItem(string Path, TrackKind Kind, string Title, string? Advertiser, double Seconds, bool InBreak);

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
    /// <summary>Events and breaks due this soon are told to the engine (for the gap before them).</summary>
    static readonly TimeSpan Expect = TimeSpan.FromMinutes(15);

    readonly PlayoutEngine _engine;
    readonly Timer _timer;
    readonly object _lock = new();
    ScheduleData _data;
    DateTime _prepared = DateTime.MinValue;

    /// <summary>A line for the status bar and the log (raised on the timer thread).</summary>
    public event Action<string>? Message;
    /// <summary>The schedule program saved a new schedule and it was loaded (raised on the timer thread).</summary>
    public event Action? Reloaded;

    readonly ScheduleSource? _source;
    readonly Timer? _reloadTimer;
    long _version = -1;
    int _reloading;

    /// <param name="source">The database (and the local copy) the schedule comes from; null: only <paramref name="data"/>.</param>
    public Scheduler(PlayoutEngine engine, ScheduleData data, ScheduleSource? source = null)
    {
        _engine = engine;
        data.Normalize();
        _data = data;
        _source = source;
        _timer = new Timer(_ => Check(DateTime.Now), null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));
        // The database is read on its own timer: a slow or unreachable server never holds up an event that is due.
        if (source != null) _reloadTimer = new Timer(_ => ReloadIfSaved(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    /// <summary>When each started, kept: in the database (off this thread) and in the local copy.</summary>
    void SaveState()
    {
        if (_source == null) return;
        var copy = _data.Clone();
        Task.Run(() => _source.SaveState(copy));
    }

    /// <summary>
    /// The schedule as edited (a copy is kept). Entries that were due before now don't start because of the edit. With
    /// <paramref name="trustState"/> (the first read from the database, at start-up), when each started as kept there
    /// counts too, so one due just before a restart still starts.
    /// </summary>
    public void Update(ScheduleData data, bool trustState = false)
    {
        lock (_lock)
        {
            _prepared = DateTime.MinValue; // what is coming is looked at again (and told to the engine) straight away
            var now = DateTime.Now;
            var copy = data.Clone();
            foreach (var entry in Entries(copy))
            {
                DateTime? stored = trustState && data.LastStarted.TryGetValue(entry.Id, out var s) ? s : null;
                DateTime? known = _data.LastStarted.TryGetValue(entry.Id, out var k) ? k : null;
                if (stored != null || (known != null && !Changed(entry)))
                    copy.LastStarted[entry.Id] = new[] { stored, known != null && !Changed(entry) ? known : null }.Max()!.Value;
                else copy.LastStarted[entry.Id] = now;
            }
            foreach (var id in copy.LastStarted.Keys.Where(id => Entries(copy).All(e => e.Id != id)).ToList()) copy.LastStarted.Remove(id);
            _data = copy;
            SaveState();
            _prepared = DateTime.MinValue;
        }

        bool Changed(ScheduleEntry entry)
        {
            var old = Entries(_data).FirstOrDefault(e => e.Id == entry.Id);
            return old == null || old.Enabled != entry.Enabled
                   || !old.Slots.Select(x => (x.Day, x.At)).ToHashSet().SetEquals(entry.Slots.Select(x => (x.Day, x.At)))
                   || !old.Dates.ToHashSet().SetEquals(entry.Dates);
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
            if (items.Count == 0)
            {
                // A break with no adverts is shown all the same (greyed): so it is seen that nothing is booked there.
                if (entry is AdBreak)
                    list.Add(new UpcomingEvent(entry.Id, Label(entry, at), L.T("no adverts booked: it will be skipped"), at, at, entry.Start, 0, true, entry.MaxDelayMinutes, Empty: true));
                continue; // a missing file: nothing will play
            }
            var parts = items.Select(i => i.StreamName != null
                ? new UpcomingItem(i.Path, TrackKind.Stream, i.StreamName, null, i.StreamSeconds, i.InBreak)
                : new UpcomingItem(i.Path, i.Kind ?? TrackClassifier.Classify(i.Path), System.IO.Path.GetFileNameWithoutExtension(i.Path), i.Advertiser, Length(i.Path), i.InBreak)).ToList();
            double seconds = parts.Sum(p => p.Seconds);
            string what = entry switch
            {
                AdBreak => items.Count(i => i.Kind == TrackKind.Commercial) is var n && n == 1 ? L.T("1 advert") : L.F("{0} adverts", n),
                _ => items[0].StreamName != null ? L.T("stream") : System.IO.Path.GetFileNameWithoutExtension(items[0].Path),
            };
            var start = entry.Start == EventStart.EndAt ? at.AddSeconds(-seconds) : at;
            list.Add(new UpcomingEvent(entry.Id, Label(entry, at), what, start, at, entry.Start, seconds, entry is AdBreak, entry.MaxDelayMinutes, Items: parts));
        }
        return list;
    }

    readonly Dictionary<string, double> _lengths = new();

    double Length(string path)
    {
        lock (_lengths)
        {
            if (_lengths.TryGetValue(path, out double s)) return s;
            s = SharedPaths.FileExists(path) ? AudioProbe.Duration(path) ?? 0 : 0;
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

    /// <summary>
    /// Every couple of seconds (its own timer): when the schedule in the database has a new version (the schedule program
    /// saved it), it takes over. While the database can't be reached, the schedule already loaded carries on.
    /// </summary>
    void ReloadIfSaved()
    {
        if (_source == null || Interlocked.Exchange(ref _reloading, 1) == 1) return;
        try
        {
            if (_source.Version() is not { } version || version == _version) return;
            bool first = _version < 0;
            if (_source.Load() is not { } data) return;
            _version = version;
            // The first time, the local copy (or the old files) may already have been loaded: only a real change is news.
            bool changed = !Same(data);
            Update(data, trustState: first);
            if (changed && !first) Say(L.T("Schedule updated"));
            Reloaded?.Invoke();
        }
        catch (Exception ex) { Say(L.F("Schedule not loaded: {0}", ex.Message)); }
        finally { Volatile.Write(ref _reloading, 0); }
    }

    bool Same(ScheduleData data)
    {
        lock (_lock)
        {
            static string Text(ScheduleData d) { var c = d.Clone(); c.LastStarted.Clear(); return JsonSerializer.Serialize(c); }
            return Text(_data) == Text(data);
        }
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
            if (due.Count > 0) SaveState();
        }
        // Breaks first when several are due at once: they are booked to the second, events can wait.
        foreach (var (entry, at) in due.OrderBy(d => d.At).ThenBy(d => d.Entry is AdBreak ? 0 : 1))
            Start(entry, at);
    }

    /// <summary>
    /// Measures (for loudness levelling) the files of what starts in the next few minutes, and tells the engine when the
    /// coming events and breaks are due (a short gap before one gets a filler). Not those that end at their time (they
    /// are backtimed), nor a break with nothing booked (it is skipped).
    /// </summary>
    void PrepareSoon(DateTime now)
    {
        _prepared = now;
        var expected = new List<DateTime>();
        foreach (var entry in Entries(_data).Where(e => e.Enabled && e.Start != EventStart.EndAt))
            foreach (var at in entry.TimesOn(now.Date).Concat(entry.TimesOn(now.Date.AddDays(1)))
                         .Where(t => t > now && t - now <= Expect && t > _data.LastStarted.GetValueOrDefault(entry.Id, DateTime.MinValue)))
                if (entry is not AdBreak b || _data.InBreak(b, at).Count > 0) expected.Add(at.ToUniversalTime());
        _engine.ExpectedEvents = expected;
        var files = new List<string>();
        foreach (var entry in Entries(_data).Where(e => e.Enabled))
            foreach (var at in entry.TimesOn(now.Date).Concat(entry.TimesOn(now.Date.AddDays(1))).Where(t => t > now && t - now <= Prepare))
                files.AddRange(Items(entry, at, quiet: true).Where(i => i.StreamName == null).Select(i => i.Path));
        _engine.MeasureLoudness(files.Distinct());
    }

    /// <summary>Another computer is on air: its breaks and events are its own, none is put in this playlist.</summary>
    public bool Standby { get; set; }

    void Start(ScheduleEntry entry, DateTime at)
    {
        if (Standby) return;
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

    static string Label(ScheduleEntry entry, DateTime at) => ScheduleItems.Label(entry, at);

    /// <summary>What an event or break plays at <paramref name="at"/>; quiet: no messages (looking ahead).</summary>
    List<ScheduledItem> Items(ScheduleEntry entry, DateTime at, bool quiet)
    {
        ScheduleData data;
        lock (_lock) data = _data;
        return ScheduleItems.For(data, entry, at, quiet ? null : Say, _source?.Db is { } db ? ScheduleItems.CategoryPicker(db) : null);
    }

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");

    public void Dispose()
    {
        _timer.Dispose();
        _reloadTimer?.Dispose();
    }
}

/// <summary>What the schedule's events and ad breaks play: for the playout, the playlist generator and the manager alike.</summary>
public static class ScheduleItems
{
    public static string Label(ScheduleEntry entry, DateTime at) =>
        L.F("{0} {1:HH:mm}", (string.IsNullOrWhiteSpace(entry.Name) ? entry is AdBreak ? L.T("Ad break") : L.T("Event") : entry.Name.Trim()), at);

    /// <summary>
    /// What an event or break plays at <paramref name="at"/> (the files as they are now: a folder's newest file, the
    /// adverts booked). <paramref name="say"/> gets what is left out and why; null: quiet (looking ahead).
    /// </summary>
    /// <param name="fromCategory">
    /// Picks a file from a category for a part that takes one, not one of the files given (the break's other parts
    /// took them); null: no library, left out.
    /// </param>
    public static List<ScheduledItem> For(ScheduleData data, ScheduleEntry entry, DateTime at, Action<string>? say,
        Func<long, IReadOnlySet<string>, (string Path, TrackKind Kind)?>? fromCategory = null)
    {
        bool quiet = say == null;
        void Say(string message) => say?.Invoke(message);
        var items = new List<ScheduledItem>();
        string label = Label(entry, at);
        switch (entry)
        {
            case ScheduledEvent e:
                if (PlayoutEngine.IsStreamSource(e.Path))
                    items.Add(new ScheduledItem(e.Path.Trim(), StreamName: string.IsNullOrWhiteSpace(e.Name) ? e.Path.Trim() : e.Name.Trim(),
                        StreamSeconds: Math.Max(0, e.StreamMinutes) * 60));
                else if (SharedPaths.DirectoryExists(e.Path))
                {
                    var newest = AudioFiles.InFolder(e.Path).Select(f => new FileInfo(SharedPaths.ToLocal(f))).MaxBy(f => f.LastWriteTimeUtc);
                    if (newest == null) { if (!quiet) Say(L.F("{0}: no audio files in {1}", label, e.Path)); }
                    else if (e.MaxAgeMinutes > 0 && DateTime.UtcNow - newest.LastWriteTimeUtc > TimeSpan.FromMinutes(e.MaxAgeMinutes))
                    {
                        if (!quiet) Say(L.F("{0}: skipped, the newest file ({1}) is older than {2} minutes", label, newest.Name, e.MaxAgeMinutes));
                    }
                    else items.Add(new ScheduledItem(SharedPaths.ToLibrary(newest.FullName), EventKind(newest.FullName)));
                }
                else if (SharedPaths.FileExists(e.Path)) items.Add(new ScheduledItem(e.Path, EventKind(e.Path)));
                else if (!quiet) Say(string.IsNullOrWhiteSpace(e.Path) ? L.F("{0}: nothing chosen to play", label) : L.F("{0}: not found: {1}", label, e.Path));
                break;

            case AdBreak b:
                var sections = b.SectionNames.Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToDictionary(n => n, n => data.InSection(b, n, at), StringComparer.CurrentCultureIgnoreCase);
                var missing = sections.Values.SelectMany(v => v).Where(a => !SharedPaths.FileExists(a.File)).ToList();
                if (!quiet) foreach (var a in missing) Say(L.F("{0}: advert not found, left out: {1}", label, a.File));
                foreach (var list in sections.Values) list.RemoveAll(missing.Contains);
                if (sections.Values.All(v => v.Count == 0))
                {
                    if (!quiet) Say(L.F("{0}: no adverts booked, skipped", label));
                    break;
                }
                for (int i = 0; i < b.Parts.Count; i++)
                {
                    var part = b.Parts[i];
                    if (part.Kind == PartKind.Section)
                    {
                        foreach (var a in sections.GetValueOrDefault(part.Section.Trim()) ?? [])
                            items.Add(new ScheduledItem(a.File, TrackKind.Commercial, Advertiser: a.Client.Name.Trim() is { Length: > 0 } n ? n : null, InBreak: true));
                        continue;
                    }
                    // A section opener: only when the section after it has adverts.
                    if (part.OnlyWithAdverts && b.Parts.Skip(i + 1).FirstOrDefault(x => x.Kind == PartKind.Section) is { } next
                        && (sections.GetValueOrDefault(next.Section.Trim())?.Count ?? 0) == 0) continue;
                    if (part.CategoryId is { } category)
                    {
                        // Not a file the break plays already (two parts from Jingles: two different jingles).
                        var taken = items.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
                        if (fromCategory?.Invoke(category, taken) is { } picked) items.Add(new ScheduledItem(picked.Path, picked.Kind, InBreak: true));
                        else if (!quiet) Say(L.F("{0}: nothing to take from {1}, left out", label, part.Section.Length > 0 ? part.Section : L.T("its category")));
                    }
                    else if (SharedPaths.FileExists(part.Path)) items.Add(new ScheduledItem(part.Path, TrackKind.Jingle, InBreak: true));
                    else if (!quiet && part.Path.Length > 0) Say(L.F("{0}: jingle not found, left out: {1}", label, part.Path));
                }
                break;
        }
        return items;
    }

    /// <summary>
    /// A file from a category (and the ones under it) for a break: the one rested longest, among a few at random, that
    /// may play now (song restrictions) and is there. Adverts' categories (Spots) give adverts, the rest jingles.
    /// </summary>
    public static Func<long, IReadOnlySet<string>, (string Path, TrackKind Kind)?> CategoryPicker(LibraryDb db) => (id, taken) =>
    {
        var now = DateTime.Now;
        var kind = db.Categories().FirstOrDefault(c => c.Id == id)?.Kind == "commercial" ? TrackKind.Commercial : TrackKind.Jingle;
        var tracks = db.RotationTracks([id]).Where(t => t.Rules?.AllowsAt(now) != false).ToList();
        // Not one this break plays already, while the category has another.
        if (tracks.Any(t => !taken.Contains(t.Path))) tracks = tracks.Where(t => !taken.Contains(t.Path)).ToList();
        // The least recently played third, shuffled: not always the same one, never one just played.
        var candidates = tracks.OrderBy(t => t.LastPlayed ?? DateTime.MinValue).Take(Math.Max(1, (tracks.Count + 2) / 3))
            .OrderBy(_ => Random.Shared.Next()).Concat(tracks).Take(12);
        foreach (var t in candidates)
            if (SharedPaths.FileExists(t.Path)) return (t.Path, kind);
        return null;
    };

    /// <summary>
    /// What an event's file is in the playlist: a jingle or an advert by where it is kept (a Jingles folder…); the rest
    /// (news, shows) a programme, which RDS, the website and the music reports don't take for a song.
    /// </summary>
    public static TrackKind EventKind(string path) => TrackClassifier.Classify(path) is var k && k is TrackKind.Jingle or TrackKind.Commercial ? k : TrackKind.Programme;
}
