using System.Globalization;
using Dapper;

namespace Playout.Library;

// The scheduling tables (schema 3): stations, clocks, rotation rules, commercial traffic and the daily logs.
// Documented in docs/scheduling-design.md. Dates are the station's local 'yyyy-MM-dd', times of day 'HH:mm:ss'.

public sealed class Station
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>IANA name such as Europe/Lisbon; empty = the computer's own time zone.</summary>
    public string TimeZone { get; set; } = "";
    public bool Active { get; set; } = true;

    public TimeZoneInfo Zone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim()); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }

    /// <summary>The station's date and time now.</summary>
    public DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone());
}

public sealed class Clock
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public bool Active { get; set; } = true;
}

public static class SlotKind
{
    /// <summary>Picks from a category by the rotation rules (a song, or a jingle from a jingle category…).</summary>
    public const string Pick = "pick";
    /// <summary>One particular track (the station ID).</summary>
    public const string Item = "item";
    /// <summary>A commercial break: traffic puts the booked adverts in it.</summary>
    public const string Break = "break";
    /// <summary>A file, the newest file in a folder (news), or a stream relay.</summary>
    public const string Event = "event";
    public const string Voice = "voice";
}

public static class Timing
{
    public const string None = "none", Soft = "soft", Hard = "hard", EndAt = "end_at";
}

public static class LockLevel
{
    public const string Free = "free", Movable = "movable", Locked = "locked";
}

public sealed class ClockSlot
{
    public long Id { get; set; }
    public long ClockId { get; set; }
    public int Position { get; set; }
    public string Kind { get; set; } = SlotKind.Pick;
    public long? CategoryId { get; set; }
    public long? TrackId { get; set; }
    public string? Source { get; set; }
    public string? Name { get; set; }
    /// <summary>When in the hour it is due (seconds, 0–3599); null = after the slot before.</summary>
    public double? AtSeconds { get; set; }
    public string Timing { get; set; } = Library.Timing.None;
    public double? MaxDelay { get; set; }
    public string LockLevel { get; set; } = Library.LockLevel.Free;
    public double? MaxSeconds { get; set; }
    public long? IntroTrackId { get; set; }
    public long? OutroTrackId { get; set; }
}

/// <summary>Rotation rules for a category; null = the station's default.</summary>
public sealed class RotationRule
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public long CategoryId { get; set; }
    public int? SongRestMinutes { get; set; }
    public int? ArtistRestMinutes { get; set; }
    public int? TitleRestMinutes { get; set; }
    public int? MaxPerHour { get; set; }
    public bool TempoFlow { get; set; }
    public bool VoiceFlow { get; set; }
}

public sealed class Advertiser
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Advertisers with the same code are kept out of the same break (cars, banks…).</summary>
    public string? Competition { get; set; }
    public string? Contact { get; set; }
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
}

public static class CampaignStatus
{
    public const string Booked = "booked", Paused = "paused", Cancelled = "cancelled";
}

public sealed class Campaign
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public long AdvertiserId { get; set; }
    public string Name { get; set; } = "";
    public string StartsOn { get; set; } = "";
    public string EndsOn { get; set; } = "";
    public string Status { get; set; } = CampaignStatus.Booked;
    public string? Notes { get; set; }
}

public sealed class CampaignSpot
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public long TrackId { get; set; }
    public int Weight { get; set; } = 1;
    public string? StartsOn { get; set; }
    public string? EndsOn { get; set; }
}

public sealed class Booking
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    /// <summary>Weekdays it applies to, e.g. "12345" (0 = Sunday).</summary>
    public string Days { get; set; } = "0123456";
    public int PerDay { get; set; } = 1;
    public string FromTime { get; set; } = "00:00:00";
    public string ToTime { get; set; } = "24:00:00";
    public string? BreakName { get; set; }
    /// <summary>any | first | last</summary>
    public string Position { get; set; } = "any";
    /// <summary>Only in its band (and named break); otherwise it may go to a nearby break when those are full.</summary>
    public bool Fixed { get; set; }
}

public static class LogStatus
{
    public const string Draft = "draft", Ready = "ready", OnAir = "on_air";
}

public sealed class ScheduleLog
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public string Date { get; set; } = "";
    public string Status { get; set; } = LogStatus.Draft;
    public int Revision { get; set; } = 1;
    public string MadeBy { get; set; } = "auto";
    public string? CheckedBy { get; set; }
    public string? CheckedAt { get; set; }
}

public static class ItemKind
{
    public const string Song = "song", Jingle = "jingle", Spot = "spot", Other = "other", Event = "event", Voice = "voice";
}

public static class ItemState
{
    public const string Scheduled = "scheduled", Loaded = "loaded", Aired = "aired", Missed = "missed", Removed = "removed";
}

public sealed class LogItem
{
    public long Id { get; set; }
    public long LogId { get; set; }
    public int Hour { get; set; }
    public int Position { get; set; }
    public string Kind { get; set; } = ItemKind.Song;
    public long? TrackId { get; set; }
    public string? Source { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Duration { get; set; }
    /// <summary>When it is expected to start (local).</summary>
    public string StartsAt { get; set; } = "00:00:00";
    /// <summary>Timed items: when it is due (soft, hard) or has to end (end_at).</summary>
    public string? DueAt { get; set; }
    public string Timing { get; set; } = Library.Timing.None;
    public double? MaxDelay { get; set; }
    public string LockLevel { get; set; } = Library.LockLevel.Free;
    public string? GroupKey { get; set; }
    public string? GroupName { get; set; }
    public long? ClockSlotId { get; set; }
    public long? CampaignSpotId { get; set; }
    public long? BookingId { get; set; }
    public string State { get; set; } = ItemState.Scheduled;
}

/// <summary>
/// Reads and writes the scheduling tables. Like <see cref="LibraryDb"/>, every call opens its own pooled
/// connection, so it can be used from any thread.
/// </summary>
public sealed class ScheduleStore
{
    readonly LibraryDb _db;

    static ScheduleStore() => DefaultTypeMap.MatchNamesWithUnderscores = true; // station_id → StationId

    public ScheduleStore(LibraryDb db) => _db = db;

    public LibraryDb Library => _db;

    string LastId => _db.IsMySql ? "LAST_INSERT_ID()" : "last_insert_rowid()";

    long Insert(string table, string columns, object row)
    {
        var names = columns.Split(',', StringSplitOptions.TrimEntries);
        using var c = _db.Connect();
        return c.ExecuteScalar<long>(
            $"INSERT INTO {table} ({string.Join(", ", names.Select(Column))}) VALUES ({string.Join(", ", names.Select(n => "@" + n))}); SELECT {LastId};", row);
    }

    /// <summary>A property name as its column: StationId → station_id.</summary>
    static string Column(string property) =>
        string.Concat(property.Select((ch, i) => char.IsUpper(ch) && i > 0 ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    // ───────────────────────── Stations ─────────────────────────

    public List<Station> Stations()
    {
        using var c = _db.Connect();
        return c.Query<Station>("SELECT * FROM stations ORDER BY id").ToList();
    }

    public Station? Station(long id)
    {
        using var c = _db.Connect();
        return c.QueryFirstOrDefault<Station>("SELECT * FROM stations WHERE id = @id", new { id });
    }

    /// <summary>A station by id or name (ignoring case); null if there is none.</summary>
    public Station? FindStation(string idOrName) =>
        long.TryParse(idOrName, out long id) ? Station(id)
        : Stations().FirstOrDefault(s => s.Name.Equals(idOrName.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public long AddStation(Station s) => Insert("stations", "Name, TimeZone, Active", s);

    public void UpdateStation(Station s)
    {
        using var c = _db.Connect();
        c.Execute("UPDATE stations SET name = @Name, time_zone = @TimeZone, active = @Active WHERE id = @Id", s);
    }

    public string? Setting(long stationId, string name)
    {
        using var c = _db.Connect();
        return c.QueryFirstOrDefault<string>("SELECT value FROM schedule_settings WHERE station_id = @stationId AND name = @name", new { stationId, name });
    }

    public void SetSetting(long stationId, string name, string value)
    {
        using var c = _db.Connect();
        c.Execute(_db.IsMySql
            ? "INSERT INTO schedule_settings (station_id, name, value) VALUES (@stationId, @name, @value) ON DUPLICATE KEY UPDATE value = @value"
            : "INSERT INTO schedule_settings (station_id, name, value) VALUES (@stationId, @name, @value) ON CONFLICT (station_id, name) DO UPDATE SET value = @value",
            new { stationId, name, value });
    }

    public int IntSetting(long stationId, string name, int fallback) =>
        int.TryParse(Setting(stationId, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    // ───────────────────────── Clocks ─────────────────────────

    public List<Clock> Clocks(long stationId)
    {
        using var c = _db.Connect();
        return c.Query<Clock>("SELECT * FROM clocks WHERE station_id = @stationId ORDER BY name", new { stationId }).ToList();
    }

    public long AddClock(Clock clock) => Insert("clocks", "StationId, Name, Color, Active", clock);

    public List<ClockSlot> Slots(long clockId)
    {
        using var c = _db.Connect();
        return c.Query<ClockSlot>("SELECT * FROM clock_slots WHERE clock_id = @clockId ORDER BY position, id", new { clockId }).ToList();
    }

    public long AddSlot(ClockSlot slot) => Insert("clock_slots",
        "ClockId, Position, Kind, CategoryId, TrackId, Source, Name, AtSeconds, Timing, MaxDelay, LockLevel, MaxSeconds, IntroTrackId, OutroTrackId", slot);

    /// <summary>Gives hours of the week a clock (weekday 0 = Sunday).</summary>
    public void SetGrid(long stationId, IEnumerable<(int Weekday, int Hour)> hours, long clockId)
    {
        using var c = _db.Connect();
        using var tx = c.BeginTransaction();
        foreach (var (weekday, hour) in hours)
        {
            c.Execute("DELETE FROM clock_grid WHERE station_id = @stationId AND weekday = @weekday AND hour = @hour", new { stationId, weekday, hour }, tx);
            c.Execute("INSERT INTO clock_grid (station_id, weekday, hour, clock_id) VALUES (@stationId, @weekday, @hour, @clockId)", new { stationId, weekday, hour, clockId }, tx);
        }
        tx.Commit();
    }

    public void SetOverride(long stationId, DateOnly date, int hour, long clockId)
    {
        using var c = _db.Connect();
        using var tx = c.BeginTransaction();
        var args = new { stationId, date = Date(date), hour, clockId };
        c.Execute("DELETE FROM clock_overrides WHERE station_id = @stationId AND date = @date AND hour = @hour", args, tx);
        c.Execute("INSERT INTO clock_overrides (station_id, date, hour, clock_id) VALUES (@stationId, @date, @hour, @clockId)", args, tx);
        tx.Commit();
    }

    /// <summary>The clock of each hour of a day (override, else the week grid); hours without one are left out.</summary>
    public Dictionary<int, long> ClocksOn(long stationId, DateOnly date)
    {
        using var c = _db.Connect();
        var hours = c.Query<(int Hour, long ClockId)>(
                "SELECT hour, clock_id FROM clock_grid WHERE station_id = @stationId AND weekday = @weekday",
                new { stationId, weekday = (int)date.DayOfWeek })
            .ToDictionary(r => r.Hour, r => r.ClockId);
        foreach (var (hour, clockId) in c.Query<(int Hour, long ClockId)>(
                     "SELECT hour, clock_id FROM clock_overrides WHERE station_id = @stationId AND date = @date", new { stationId, date = Date(date) }))
            hours[hour] = clockId;
        return hours;
    }

    public void SetRule(RotationRule rule)
    {
        using var c = _db.Connect();
        c.Execute("DELETE FROM rotation_rules WHERE station_id = @StationId AND category_id = @CategoryId", rule);
        rule.Id = Insert("rotation_rules",
            "StationId, CategoryId, SongRestMinutes, ArtistRestMinutes, TitleRestMinutes, MaxPerHour, TempoFlow, VoiceFlow", rule);
    }

    public List<RotationRule> Rules(long stationId)
    {
        using var c = _db.Connect();
        return c.Query<RotationRule>("SELECT * FROM rotation_rules WHERE station_id = @stationId", new { stationId }).ToList();
    }

    // ───────────────────────── Traffic ─────────────────────────

    public long AddAdvertiser(Advertiser a) => Insert("advertisers", "Name, Competition, Contact, Notes, Active", a);

    public List<Advertiser> Advertisers()
    {
        using var c = _db.Connect();
        return c.Query<Advertiser>("SELECT * FROM advertisers ORDER BY name").ToList();
    }

    public long AddCampaign(Campaign campaign)
    {
        string now = LibraryDb.Now();
        using var c = _db.Connect();
        return c.ExecuteScalar<long>($"""
            INSERT INTO campaigns (station_id, advertiser_id, name, starts_on, ends_on, status, notes, created_at, updated_at)
            VALUES (@StationId, @AdvertiserId, @Name, @StartsOn, @EndsOn, @Status, @Notes, @now, @now);
            SELECT {LastId};
            """, new { campaign.StationId, campaign.AdvertiserId, campaign.Name, campaign.StartsOn, campaign.EndsOn, campaign.Status, campaign.Notes, now });
    }

    public void SetCampaignStatus(long campaignId, string status)
    {
        using var c = _db.Connect();
        c.Execute("UPDATE campaigns SET status = @status, updated_at = @now WHERE id = @campaignId", new { campaignId, status, now = LibraryDb.Now() });
    }

    public long AddSpot(CampaignSpot spot) => Insert("campaign_spots", "CampaignId, TrackId, Weight, StartsOn, EndsOn", spot);

    public long AddBooking(Booking b) => Insert("bookings", "CampaignId, Days, PerDay, FromTime, ToTime, BreakName, Position, Fixed", b);

    // ───────────────────────── Logs ─────────────────────────

    public ScheduleLog? Log(long stationId, DateOnly date)
    {
        using var c = _db.Connect();
        return c.QueryFirstOrDefault<ScheduleLog>("SELECT * FROM logs WHERE station_id = @stationId AND date = @date", new { stationId, date = Date(date) });
    }

    public List<ScheduleLog> Logs(long stationId, DateOnly from, DateOnly to)
    {
        using var c = _db.Connect();
        return c.Query<ScheduleLog>("SELECT * FROM logs WHERE station_id = @stationId AND date >= @from AND date <= @to ORDER BY date",
            new { stationId, from = Date(from), to = Date(to) }).ToList();
    }

    public List<LogItem> Items(long logId)
    {
        using var c = _db.Connect();
        return c.Query<LogItem>("SELECT * FROM log_items WHERE log_id = @logId ORDER BY hour, position", new { logId }).ToList();
    }

    /// <summary>Deletes a log (and its items).</summary>
    public void DeleteLog(long logId)
    {
        using var c = _db.Connect();
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM log_items WHERE log_id = @logId", new { logId }, tx);
        c.Execute("DELETE FROM logs WHERE id = @logId", new { logId }, tx);
        tx.Commit();
    }

    public void MarkChecked(long logId, string by)
    {
        using var c = _db.Connect();
        c.Execute("UPDATE logs SET checked_by = @by, checked_at = @now WHERE id = @logId", new { logId, by, now = LibraryDb.Now() });
    }

    internal System.Data.Common.DbConnection Connect() => _db.Connect();

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"HH:mm:ss" (or "HH:mm"; "24:00" is the end of the day) as seconds from midnight.</summary>
    public static double Seconds(string time)
    {
        var p = time.Split(':');
        return int.Parse(p[0], CultureInfo.InvariantCulture) * 3600 + (p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) * 60 : 0)
               + (p.Length > 2 ? double.Parse(p[2], CultureInfo.InvariantCulture) : 0);
    }

    /// <summary>Seconds from midnight as "HH:mm:ss" (up to 23:59:59).</summary>
    public static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Clamp(Math.Round(seconds), 0, 86399));
        return t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
