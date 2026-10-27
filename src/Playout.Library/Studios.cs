using Dapper;

namespace Playout.Library;

/// <summary>A computer running Sintoniza Emi for a station: when it was last seen (UTC, the database's clock) and whether it is on air.</summary>
public sealed record Studio(string Computer, DateTime SeenAt, bool OnAir, DateTime? Since, string Playing, string Version);

/// <summary>The studios of a station, and the database's time when they were read (to tell how long ago each was seen).</summary>
public sealed record StudioList(List<Studio> Studios, DateTime Now)
{
    public Studio? OnAir => Studios.FirstOrDefault(s => s.OnAir);

    /// <summary>How long ago a studio was last seen.</summary>
    public TimeSpan Silent(Studio s) => Now - s.SeenAt;
}

public sealed partial class LibraryDb
{
    /// <summary>
    /// This studio is running (every few seconds): its row is written with the database's time, what it is playing and
    /// its version. Studios not seen for 30 days are taken out.
    /// </summary>
    public void StudioSeen(long station, string computer, string playing, string version)
    {
        using var c = Connect();
        c.Execute(_mySql
            ? $"""
              INSERT INTO studios (station_id, computer, seen_at, playing, version) VALUES (@station, @computer, {NowSql}, @playing, @version)
              ON DUPLICATE KEY UPDATE seen_at = VALUES(seen_at), playing = VALUES(playing), version = VALUES(version)
              """
            : $"""
              INSERT INTO studios (station_id, computer, seen_at, playing, version) VALUES (@station, @computer, {NowSql}, @playing, @version)
              ON CONFLICT(station_id, computer) DO UPDATE SET seen_at = excluded.seen_at, playing = excluded.playing, version = excluded.version
              """,
            new { station, computer, playing = Clip(playing, 500), version = Clip(version, 40) });
        c.Execute("DELETE FROM studios WHERE station_id = @station AND on_air = 0 AND seen_at < @old",
            new { station, old = Time(DateTime.UtcNow.AddDays(-30)) });
    }

    public StudioList Studios(long station)
    {
        using var c = Connect();
        string now = c.ExecuteScalar<string>($"SELECT {NowSql}")!;
        var rows = c.Query<(string Computer, string SeenAt, long OnAir, string? Since, string Playing, string Version)>(
                "SELECT computer, seen_at, on_air, since, playing, version FROM studios WHERE station_id = @station ORDER BY computer", new { station })
            .Select(r => new Studio(r.Computer, ParseTime(r.SeenAt), r.OnAir != 0, r.Since == null ? null : ParseTime(r.Since), r.Playing, r.Version))
            .ToList();
        return new StudioList(rows, ParseTime(now));
    }

    /// <summary>
    /// Puts this studio on air (and every other one standby), all in one go so two studios never both win. With
    /// <paramref name="unlessAnswering"/>: only when no studio is on air, or the one on air hasn't been seen for that
    /// long (it stopped answering). True: this studio is on air now.
    /// </summary>
    public bool TakeOnAir(long station, string computer, TimeSpan? unlessAnswering = null)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        // MySQL: the station's rows are locked until the end, so another studio taking over at the same moment waits
        // and then finds this one on air. SQLite: one writer at a time anyway.
        string now = c.ExecuteScalar<string>($"SELECT {NowSql}", transaction: tx)!;
        var onAir = c.Query<(string Computer, string SeenAt, long OnAir)>(
                $"SELECT computer, seen_at, on_air FROM studios WHERE station_id = @station{(_mySql ? " FOR UPDATE" : "")}", new { station }, tx)
            .Where(r => r.OnAir != 0).Select(r => ((string Computer, string SeenAt)?)(r.Computer, r.SeenAt)).FirstOrDefault();
        if (onAir is { } current && current.Computer == computer)
        {
            // Still on air: seen now, so another studio doesn't take over a moment later.
            c.Execute("UPDATE studios SET seen_at = @now WHERE station_id = @station AND computer = @computer", new { station, computer, now }, tx);
            tx.Commit();
            return true;
        }
        if (unlessAnswering is { } wait && onAir is { } other && ParseTime(now) - ParseTime(other.SeenAt) < wait)
        {
            tx.Commit();
            return false;
        }
        c.Execute(_mySql
            ? "INSERT INTO studios (station_id, computer, seen_at) VALUES (@station, @computer, @now) ON DUPLICATE KEY UPDATE seen_at = VALUES(seen_at)"
            : "INSERT INTO studios (station_id, computer, seen_at) VALUES (@station, @computer, @now) ON CONFLICT(station_id, computer) DO UPDATE SET seen_at = excluded.seen_at",
            new { station, computer, now }, tx);
        c.Execute("UPDATE studios SET on_air = 0, since = NULL WHERE station_id = @station AND computer <> @computer", new { station, computer }, tx);
        c.Execute("UPDATE studios SET on_air = 1, since = @now WHERE station_id = @station AND computer = @computer", new { station, computer, now }, tx);
        tx.Commit();
        return true;
    }

    static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
