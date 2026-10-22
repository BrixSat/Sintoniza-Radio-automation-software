using System.Globalization;
using Dapper;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>An hour of a daily playlist: when it was made, from which hour template, and whether it was changed by hand.</summary>
public sealed class PlanHour
{
    /// <summary>Local time, on the hour.</summary>
    public DateTime Start { get; set; }
    public Guid? TemplateId { get; set; }
    /// <summary>The template as it was when the hour was made (see <see cref="PlaylistGenerator.Signature"/>).</summary>
    public string Signature { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = "";
    /// <summary>Changed by hand: never made again by itself.</summary>
    public bool Edited { get; set; }
    /// <summary>A note for the presenter about the whole hour (shown on air as the hour starts); null when saving: keep the one it has.</summary>
    public string? Note { get; set; }
}

/// <summary>A song or jingle planned in an hour, in order.</summary>
public sealed class PlanItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TrackKind Kind { get; set; } = TrackKind.Music;
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    /// <summary>How long it plays before the next item starts (its length less the crossfade into the next).</summary>
    public double Seconds { get; set; }
    public long? TrackId { get; set; }
    /// <summary>The template line it came from (0 = first); null: put there by hand; <see cref="KeptTime"/>: added by the playout.</summary>
    public int? Line { get; set; }

    /// <summary><see cref="Line"/> of a song the playout added on air to keep the hour on time.</summary>
    public const int KeptTime = -1;
    public long? CategoryId { get; set; }
    /// <summary>At the end of the hour, played only if there is time (the playout drops it to start the next hour on time).</summary>
    public bool Optional { get; set; }
    /// <summary>A voice track: it starts this many seconds before the item before it ends, over its ending.</summary>
    public double Overlap { get; set; }
    /// <summary>It belongs to the item before it (a voice track about that song): removed, moved and skipped with it.</summary>
    public bool FollowsPrevious { get; set; }
    /// <summary>A note for the presenter about this item (what to say, the sponsor to mention), shown on air before and while it plays.</summary>
    public string? Note { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

/// <summary>
/// The daily playlists in the library database (plan_hours, plan_items; docs/library-database.md), one hour at a time.
/// Station 1, as the schedule.
/// </summary>
public sealed class PlanStore(LibraryDb db)
{
    const long Station = 1;
    const string HourFormat = "yyyy-MM-ddTHH:00:00";

    public LibraryDb Db { get; } = db;

    public static string HourKey(DateTime hour) => hour.ToString(HourFormat, CultureInfo.InvariantCulture);

    static DateTime ParseHour(string text) => DateTime.ParseExact(text, HourFormat, CultureInfo.InvariantCulture);

    sealed class HourRow
    {
        public string HourStart { get; set; } = "";
        public string? TemplateId { get; set; }
        public string Signature { get; set; } = "";
        public string GeneratedAt { get; set; } = "";
        public string GeneratedBy { get; set; } = "";
        public long Edited { get; set; }
        public string? Note { get; set; }
    }

    sealed class ItemRow
    {
        public string Id { get; set; } = "";
        public string HourStart { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public double Seconds { get; set; }
        public long? TrackId { get; set; }
        public long? Line { get; set; }
        public long? CategoryId { get; set; }
        public long Optional { get; set; }
        public double Overlap { get; set; }
        public long Follows { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>The hours made from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive).</summary>
    public Dictionary<DateTime, PlanHour> Hours(DateTime from, DateTime to)
    {
        using var c = Db.Connect();
        return c.Query<HourRow>("""
                SELECT hour_start AS HourStart, template_id AS TemplateId, signature AS Signature, generated_at AS GeneratedAt,
                       generated_by AS GeneratedBy, edited AS Edited, note AS Note
                FROM plan_hours WHERE station_id = @Station AND hour_start >= @from AND hour_start < @to
                """, new { Station, from = HourKey(from), to = HourKey(to) })
            .Select(r => new PlanHour
            {
                Start = ParseHour(r.HourStart),
                TemplateId = Guid.TryParse(r.TemplateId, out var id) ? id : null,
                Signature = r.Signature,
                GeneratedAt = DateTime.Parse(r.GeneratedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                GeneratedBy = r.GeneratedBy,
                Edited = r.Edited != 0,
                Note = r.Note,
            })
            .ToDictionary(h => h.Start);
    }

    /// <summary>The items of the hours from <paramref name="from"/> to <paramref name="to"/>, each hour's in order.</summary>
    public Dictionary<DateTime, List<PlanItem>> Items(DateTime from, DateTime to)
    {
        using var c = Db.Connect();
        return c.Query<ItemRow>("""
                SELECT id AS Id, hour_start AS HourStart, kind AS Kind, path AS Path, title AS Title, artist AS Artist,
                       seconds AS Seconds, track_id AS TrackId, line AS Line, category_id AS CategoryId, optional AS Optional,
                       overlap AS Overlap, follows AS Follows, note AS Note
                FROM plan_items WHERE station_id = @Station AND hour_start >= @from AND hour_start < @to
                ORDER BY hour_start, position
                """, new { Station, from = HourKey(from), to = HourKey(to) })
            .GroupBy(r => ParseHour(r.HourStart))
            .ToDictionary(g => g.Key, g => g.Select(r => new PlanItem
            {
                Id = Guid.TryParse(r.Id, out var id) ? id : Guid.NewGuid(),
                Kind = Enum.TryParse<TrackKind>(r.Kind, true, out var k) ? k : TrackKind.Music,
                Path = r.Path,
                Title = r.Title,
                Artist = r.Artist,
                Seconds = r.Seconds,
                TrackId = r.TrackId,
                Line = r.Line is { } l ? (int)l : null,
                CategoryId = r.CategoryId,
                Optional = r.Optional != 0,
                Overlap = r.Overlap,
                FollowsPrevious = r.Follows != 0,
                Note = r.Note,
            }).ToList());
    }

    public List<PlanItem> Items(DateTime hour) => Items(hour, hour.AddHours(1)).GetValueOrDefault(hour) ?? new();

    /// <summary>
    /// Saves an hour and its items, in place of what it had. With <paramref name="unlessEdited"/>, an hour someone
    /// changed by hand meanwhile is left as it is (returns false).
    /// </summary>
    public bool Save(PlanHour hour, IReadOnlyList<PlanItem> items, bool unlessEdited = false)
    {
        string key = HourKey(hour.Start);
        using var c = Db.Connect();
        using var tx = c.BeginTransaction();
        if (unlessEdited && c.ExecuteScalar<long?>("SELECT edited FROM plan_hours WHERE station_id = @Station AND hour_start = @key",
                new { Station, key }, tx) == 1)
            return false;
        // The hour's note stays when the hour is made again (a new PlanHour has none).
        string? note = hour.Note ?? c.ExecuteScalar<string?>("SELECT note FROM plan_hours WHERE station_id = @Station AND hour_start = @key", new { Station, key }, tx);
        c.Execute("DELETE FROM plan_items WHERE station_id = @Station AND hour_start = @key", new { Station, key }, tx);
        c.Execute("DELETE FROM plan_hours WHERE station_id = @Station AND hour_start = @key", new { Station, key }, tx);
        c.Execute("""
            INSERT INTO plan_hours (station_id, hour_start, template_id, signature, generated_at, generated_by, edited, note)
            VALUES (@Station, @key, @template, @Signature, @at, @GeneratedBy, @edited, @note)
            """, new
        {
            Station, key, template = hour.TemplateId?.ToString(), hour.Signature, at = LibraryDb.Time(hour.GeneratedAt),
            hour.GeneratedBy, edited = hour.Edited ? 1 : 0, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        }, tx);
        c.Execute("""
            INSERT INTO plan_items (id, station_id, hour_start, position, kind, path, title, artist, seconds, track_id, line, category_id, optional, overlap, follows, note)
            VALUES (@id, @Station, @key, @position, @kind, @Path, @Title, @Artist, @Seconds, @TrackId, @Line, @CategoryId, @optional, @Overlap, @follows, @note)
            """, items.Select((i, n) => new
        {
            id = i.Id.ToString(), Station, key, position = n, kind = i.Kind.ToString().ToLowerInvariant(), i.Path, i.Title, i.Artist,
            i.Seconds, i.TrackId, i.Line, i.CategoryId, optional = i.Optional ? 1 : 0, i.Overlap, follows = i.FollowsPrevious ? 1 : 0,
            note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim(),
        }), tx);
        tx.Commit();
        return true;
    }

    /// <summary>
    /// What the playout did on air to keep time, so the plan shows what really plays: songs added at the end of an hour
    /// (<see cref="PlanItem.KeptTime"/>), and an optional item left out (<paramref name="removePath"/>, its last one).
    /// Hours that weren't made (nothing planned) are left alone.
    /// </summary>
    public void KeepTime(DateTime hour, IReadOnlyList<PlanItem> added, string? removePath = null)
    {
        string key = HourKey(hour);
        using var c = Db.Connect();
        using var tx = c.BeginTransaction();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM plan_hours WHERE station_id = @Station AND hour_start = @key", new { Station, key }, tx) == 0) return;
        if (removePath != null)
        {
            var id = c.ExecuteScalar<string?>("""
                SELECT id FROM plan_items WHERE station_id = @Station AND hour_start = @key AND path = @removePath AND optional = 1
                ORDER BY position DESC LIMIT 1
                """, new { Station, key, removePath }, tx);
            if (id != null) c.Execute("DELETE FROM plan_items WHERE id = @id", new { id }, tx);
        }
        int position = c.ExecuteScalar<int?>("SELECT MAX(position) FROM plan_items WHERE station_id = @Station AND hour_start = @key", new { Station, key }, tx) ?? -1;
        c.Execute("""
            INSERT INTO plan_items (id, station_id, hour_start, position, kind, path, title, artist, seconds, track_id, line, category_id, optional)
            VALUES (@id, @Station, @key, @position, @kind, @Path, @Title, @Artist, @Seconds, @TrackId, @Line, @CategoryId, 1)
            """, added.Select((i, n) => new
        {
            id = i.Id.ToString(), Station, key, position = position + 1 + n, kind = i.Kind.ToString().ToLowerInvariant(), i.Path, i.Title, i.Artist,
            i.Seconds, i.TrackId, Line = PlanItem.KeptTime, i.CategoryId,
        }), tx);
        tx.Commit();
    }

    /// <summary>The presenter's note for an hour ("" or null: none). The hour must have been made.</summary>
    public bool SetHourNote(DateTime hour, string? note)
    {
        using var c = Db.Connect();
        return c.Execute("UPDATE plan_hours SET note = @note WHERE station_id = @Station AND hour_start = @key",
            new { Station, key = HourKey(hour), note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() }) > 0;
    }

    /// <summary>A file's title and artist as its tags now say, in every hour it is planned in.</summary>
    public int UpdateTitles(string path, string title, string artist)
    {
        using var c = Db.Connect();
        return c.Execute("UPDATE plan_items SET title = @title, artist = @artist WHERE path = @path", new { path, title, artist });
    }

    /// <summary>How long a file plays in the hours from <paramref name="from"/> (its cue points changed).</summary>
    public int UpdateSeconds(string path, double seconds, DateTime from)
    {
        using var c = Db.Connect();
        return c.Execute("UPDATE plan_items SET seconds = @seconds WHERE station_id = @Station AND path = @path AND hour_start >= @from",
            new { Station, path, seconds, from = HourKey(from) });
    }

    /// <summary>Removes the hours before <paramref name="before"/> (what played is in the as-run log).</summary>
    public int Prune(DateTime before)
    {
        using var c = Db.Connect();
        c.Execute("DELETE FROM plan_items WHERE station_id = @Station AND hour_start < @before", new { Station, before = HourKey(before) });
        return c.Execute("DELETE FROM plan_hours WHERE station_id = @Station AND hour_start < @before", new { Station, before = HourKey(before) });
    }
}
