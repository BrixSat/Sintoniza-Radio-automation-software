namespace Playout.Engine;

/// <summary>
/// The station's clock: what time it is where the station is on air (its time zone, Sintoniza Pro → Station settings),
/// which may not be this computer's (a server in another country, a studio for a station in the Azores). Every time
/// the programs plan, show, log or keep is the station's; UTC is turned into it and back here. Without a time zone of
/// its own (the usual case): the computer's, exactly as <see cref="DateTime.Now"/>.
/// </summary>
public static class StationTime
{
    static TimeZoneInfo _zone = TimeZoneInfo.Local;
    static string _name = "";
    static bool _isComputers = true;

    /// <summary>The station's time zone (the computer's when it has none of its own).</summary>
    public static TimeZoneInfo Zone => _zone;

    /// <summary>Its IANA name ("Atlantic/Azores"); "" for the computer's own.</summary>
    public static string Name => _name;

    /// <summary>The station's time is the computer's (no time zone of its own, or the same one).</summary>
    public static bool IsComputers => _isComputers;

    /// <summary>The time zone named (IANA, or Windows), or null when this computer doesn't know it; "" is the computer's.</summary>
    public static TimeZoneInfo? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(name.Trim()); }
        catch (Exception) { return null; }
    }

    /// <summary>The station's time from now on; false (and the computer's) when the zone isn't known here.</summary>
    public static bool Use(string? name)
    {
        var zone = Find(name);
        _zone = zone ?? TimeZoneInfo.Local;
        _name = zone == null || string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
        // The computer's own, under any name (Windows calls Lisbon "GMT Standard Time"): its clock as it is.
        _isComputers = _zone == TimeZoneInfo.Local || _zone.HasSameRules(TimeZoneInfo.Local);
        return zone != null;
    }

    /// <summary>The station's time now (as the computer's local time when it is the computer's).</summary>
    public static DateTime Now => IsComputers ? DateTime.Now : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone);

    /// <summary>The station's date today.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>A time of the station's (or UTC) in UTC; a time the clocks skip (spring forward) as the one after the jump.</summary>
    public static DateTime ToUtc(DateTime time)
    {
        if (time.Kind == DateTimeKind.Utc) return time;
        // The computer's clock (and a time that says it is the computer's, kept from before): as it always was.
        if (IsComputers || time.Kind == DateTimeKind.Local) return time.ToUniversalTime();
        var wall = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        if (_zone.IsInvalidTime(wall))
        {
            // Skipped by the clocks: moved on by the jump (an hour, half an hour on Lord Howe Island).
            var jump = _zone.GetUtcOffset(wall.AddHours(12)) - _zone.GetUtcOffset(wall.AddHours(-12));
            wall += jump > TimeSpan.Zero ? jump : TimeSpan.FromHours(1);
            if (_zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(wall, _zone);
    }

    /// <summary>A UTC time (Kind not Local: taken as UTC, as <see cref="DateTime.ToLocalTime"/> does) as the station's.</summary>
    public static DateTime FromUtc(DateTime time)
    {
        if (IsComputers) return time.ToLocalTime();
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _zone);
    }
}
