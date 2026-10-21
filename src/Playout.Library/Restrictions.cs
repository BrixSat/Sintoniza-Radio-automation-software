using System.Globalization;
using Dapper;

namespace Playout.Library;

/// <summary>
/// When a song may be picked automatically (the daily playlists, fill music): between two dates, on some days, at some
/// hours, and at most so many times a day or a week. Added by hand, it plays all the same.
/// </summary>
/// <param name="Days">7 characters, Monday to Sunday, '1' = may play; null: every day.</param>
/// <param name="Hours">24 characters, 00h to 23h, '1' = may play; null: every hour.</param>
public sealed record SongRestrictions(DateTime? From = null, DateTime? Until = null, string? Days = null, string? Hours = null,
    int? MaxPerDay = null, int? MaxPerWeek = null)
{
    public bool IsEmpty => From == null && Until == null && Days == null && Hours == null && MaxPerDay == null && MaxPerWeek == null;

    /// <summary>Whether its date, day and hour allow it at <paramref name="at"/> (local time); the counts are the caller's.</summary>
    public bool AllowsAt(DateTime at) =>
        (From is not { } from || at.Date >= from.Date)
        && (Until is not { } until || at.Date <= until.Date)
        && (Days is not { Length: 7 } days || days[DayIndex(at)] == '1')
        && (Hours is not { Length: 24 } hours || hours[at.Hour] == '1');

    /// <summary>Monday 0 … Sunday 6.</summary>
    public static int DayIndex(DateTime at) => ((int)at.DayOfWeek + 6) % 7;

    /// <summary>The Monday the week of <paramref name="at"/> starts on.</summary>
    public static DateTime WeekStart(DateTime at) => at.Date.AddDays(-DayIndex(at));

    /// <summary>A mask as kept (null when everything is allowed).</summary>
    public static string? Mask(IReadOnlyList<bool> allowed) => allowed.All(x => x) ? null : string.Concat(allowed.Select(x => x ? '1' : '0'));

    internal static SongRestrictions? Make(string? from, string? until, string? days, string? hours, int? maxDay, int? maxWeek)
    {
        var r = new SongRestrictions(Date(from), Date(until), days is { Length: 7 } ? days : null, hours is { Length: 24 } ? hours : null,
            maxDay is > 0 ? maxDay : null, maxWeek is > 0 ? maxWeek : null);
        return r.IsEmpty ? null : r;
    }

    static DateTime? Date(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    internal static string? Text(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed partial class LibraryDb
{
    /// <summary>Sets (or with null clears) when these songs may be picked automatically.</summary>
    public void SetRestrictions(IEnumerable<long> trackIds, SongRestrictions? r)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute("""
            UPDATE tracks SET play_from = @from, play_until = @until, play_days = @days, play_hours = @hours,
                              max_day = @maxDay, max_week = @maxWeek, updated_at = @now WHERE id = @id
            """,
            trackIds.Select(id => new
            {
                id, now = Now(), from = SongRestrictions.Text(r?.From), until = SongRestrictions.Text(r?.Until), days = r?.Days, hours = r?.Hours,
                maxDay = r?.MaxPerDay, maxWeek = r?.MaxPerWeek,
            }), tx);
        tx.Commit();
    }
}
