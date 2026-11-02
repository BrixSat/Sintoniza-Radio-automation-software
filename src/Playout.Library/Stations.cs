using Dapper;

namespace Playout.Library;

/// <summary>A station in the library: its number (station_id everywhere) and its name.</summary>
public sealed record StationInfo(long Id, string Name);

public sealed partial class LibraryDb
{
    /// <summary>The stations in the library, in the order they were made.</summary>
    public List<StationInfo> Stations()
    {
        using var c = Connect();
        return c.Query<(long Id, string Name)>("SELECT id, name FROM stations WHERE active = 1 ORDER BY id")
            .Select(r => new StationInfo(r.Id, r.Name)).ToList();
    }

    /// <summary>
    /// A new station; its number. A name already taken gets a number after it. <paramref name="settings"/>: its
    /// settings (station_settings) from its id and name, written in the same go, so there is never a station without them.
    /// </summary>
    public long AddStation(string name, Func<long, string, string>? settings = null)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        string unique = UniqueStationName(c, tx, name.Trim().Length > 0 ? name.Trim() : "Station", null);
        c.Execute("INSERT INTO stations (name) VALUES (@unique)", new { unique }, tx);
        long id = c.ExecuteScalar<long>(_mySql ? "SELECT LAST_INSERT_ID()" : "SELECT last_insert_rowid()", transaction: tx);
        if (settings?.Invoke(id, unique) is { } json)
            c.Execute("INSERT INTO station_settings (station_id, settings, version, updated_at, updated_by) VALUES (@id, @json, 1, @now, 'new station')",
                new { id, json, now = Now() }, tx);
        tx.Commit();
        return id;
    }

    /// <summary>A station's name (the one its settings give it); a name another station has gets a number after it.</summary>
    public void RenameStation(long id, string name)
    {
        if (name.Trim().Length == 0) return;
        using var c = Connect();
        using var tx = c.BeginTransaction();
        string unique = UniqueStationName(c, tx, name.Trim(), id);
        c.Execute("UPDATE stations SET name = @unique WHERE id = @id", new { unique, id }, tx);
        tx.Commit();
    }

    static string UniqueStationName(System.Data.IDbConnection c, System.Data.IDbTransaction tx, string name, long? except)
    {
        name = Clip(name, 100);
        // Compared without case or accents, as MySQL's unique key does ("Radio Dois" and "Rádio Dois" clash there).
        var taken = c.Query<string>("SELECT name FROM stations WHERE id <> @except", new { except = except ?? -1 }, tx)
            .Select(Normalize).ToHashSet();
        if (!taken.Contains(Normalize(name))) return name;
        for (int n = 2; ; n++)
            if (!taken.Contains(Normalize($"{name} ({n})"))) return Clip($"{name} ({n})", 100);
    }
}
