using System.Data.Common;
using System.Globalization;
using Dapper;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The station's schedule in the library database (schema 4): events, ad breaks with their running order, smart
/// playlists, clients and campaigns. The schedule program saves it all at once (a new version); the playout reads it
/// when the version changes, and keeps when each event and break last started in its own table (not touched by a save).
/// </summary>
sealed class ScheduleStore(LibraryDb db)
{
    /// <summary>The database it reads and saves.</summary>
    public LibraryDb Db => db;

    /// <summary>One station for now (the stations table can hold more).</summary>
    public const long Station = 1;
    /// <summary>How the one-off times of an event are kept (local).</summary>
    const string DateFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Someone else saved the schedule after it was opened.</summary>
    public sealed class ChangedException(DateTime at, string by) : Exception
    {
        public DateTime At { get; } = at;
        public string By { get; } = by;
    }

    sealed class VersionRow
    {
        public long Version { get; set; }
        public string UpdatedAt { get; set; } = "";
        public string UpdatedBy { get; set; } = "";
    }
    sealed class EntryRow
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public long Enabled { get; set; }
        public string StartMode { get; set; } = "";
        public double MaxDelayMinutes { get; set; }
        public string LockLevel { get; set; } = "";
        public string Path { get; set; } = "";
        public double StreamMinutes { get; set; }
        public long MaxAgeMinutes { get; set; }
        public double ArtistHours { get; set; }
        public double AlbumHours { get; set; }
        public long MaxSlowInRow { get; set; }
    }
    sealed class SlotRow
    {
        public string EntryId { get; set; } = "";
        public long Weekday { get; set; }
        public string Time { get; set; } = "";
    }
    sealed class DateRow
    {
        public string EntryId { get; set; } = "";
        public string At { get; set; } = "";
    }
    sealed class PartRow
    {
        public string EntryId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Path { get; set; } = "";
        public string Section { get; set; } = "";
        public long OnlyWithAdverts { get; set; }
    }
    sealed class LineRow
    {
        public string EntryId { get; set; } = "";
        public string Source { get; set; } = "";
        public long? CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Folder { get; set; } = "";
        public int? TempoMin { get; set; }
        public int? TempoMax { get; set; }
        public int? EnergyMin { get; set; }
        public int? EnergyMax { get; set; }
    }
    sealed class ClientRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long Enabled { get; set; }
    }
    sealed class CampaignRow
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string Name { get; set; } = "";
        public long Enabled { get; set; }
        public string? StartsOn { get; set; }
        public string? EndsOn { get; set; }
        public string Section { get; set; } = "";
        public long Place { get; set; }
    }
    sealed class FileRow
    {
        public string CampaignId { get; set; } = "";
        public string Path { get; set; } = "";
    }
    sealed class PlacementRow
    {
        public string CampaignId { get; set; } = "";
        public long? Weekday { get; set; }
        public string? Date { get; set; }
        public string Time { get; set; } = "";
    }
    sealed class StateRow
    {
        public string EntryId { get; set; } = "";
        public string LastStarted { get; set; } = "";
    }

    /// <summary>The schedule's version (0 = never saved to the database).</summary>
    public long Version()
    {
        using var c = db.Connect();
        return c.ExecuteScalar<long?>("SELECT version FROM schedule_versions WHERE station_id = @Station", new { Station }) ?? 0;
    }

    /// <summary>The schedule and its version (without when entries last started: see <see cref="LoadState"/>).</summary>
    public (ScheduleData Data, long Version) Load()
    {
        using var c = db.Connect();
        using var tx = c.BeginTransaction(); // one consistent picture, even while it is being saved
        var p = new { Station };
        long version = c.ExecuteScalar<long?>("SELECT version FROM schedule_versions WHERE station_id = @Station", p, tx) ?? 0;
        var entries = c.Query<EntryRow>(
            """
            SELECT id AS Id, type AS Type, name AS Name, enabled AS Enabled, start_mode AS StartMode, max_delay_minutes AS MaxDelayMinutes,
                   lock_level AS LockLevel, path AS Path, stream_minutes AS StreamMinutes, max_age_minutes AS MaxAgeMinutes,
                   artist_hours AS ArtistHours, album_hours AS AlbumHours, max_slow_in_row AS MaxSlowInRow
            FROM schedule_entries WHERE station_id = @Station ORDER BY type, position
            """, p, tx).ToList();
        var slots = c.Query<SlotRow>(
            "SELECT s.entry_id AS EntryId, s.weekday AS Weekday, s.time AS Time FROM schedule_slots s JOIN schedule_entries e ON e.id = s.entry_id WHERE e.station_id = @Station ORDER BY s.weekday, s.time",
            p, tx).ToLookup(r => r.EntryId);
        var dates = c.Query<DateRow>(
            "SELECT d.entry_id AS EntryId, d.at AS At FROM schedule_dates d JOIN schedule_entries e ON e.id = d.entry_id WHERE e.station_id = @Station ORDER BY d.at",
            p, tx).ToLookup(r => r.EntryId);
        var parts = c.Query<PartRow>(
            """
            SELECT b.entry_id AS EntryId, b.kind AS Kind, b.path AS Path, b.section AS Section, b.only_with_adverts AS OnlyWithAdverts
            FROM break_parts b JOIN schedule_entries e ON e.id = b.entry_id WHERE e.station_id = @Station ORDER BY b.position
            """, p, tx).ToLookup(r => r.EntryId);
        var lines = c.Query<LineRow>(
            """
            SELECT l.entry_id AS EntryId, l.source AS Source, l.category_id AS CategoryId, l.category_name AS CategoryName, l.kind AS Kind, l.folder AS Folder,
                   l.tempo_min AS TempoMin, l.tempo_max AS TempoMax, l.energy_min AS EnergyMin, l.energy_max AS EnergyMax
            FROM playlist_lines l JOIN schedule_entries e ON e.id = l.entry_id WHERE e.station_id = @Station ORDER BY l.position
            """, p, tx).ToLookup(r => r.EntryId);
        var clients = c.Query<ClientRow>("SELECT id AS Id, name AS Name, enabled AS Enabled FROM ad_clients WHERE station_id = @Station ORDER BY position", p, tx).ToList();
        var campaigns = c.Query<CampaignRow>(
            """
            SELECT k.id AS Id, k.client_id AS ClientId, k.name AS Name, k.enabled AS Enabled, k.starts_on AS StartsOn, k.ends_on AS EndsOn,
                   k.section AS Section, k.place AS Place
            FROM ad_campaigns k JOIN ad_clients c ON c.id = k.client_id WHERE c.station_id = @Station ORDER BY k.position
            """, p, tx).ToLookup(r => r.ClientId);
        var files = c.Query<FileRow>(
            """
            SELECT f.campaign_id AS CampaignId, f.path AS Path FROM ad_campaign_files f
            JOIN ad_campaigns k ON k.id = f.campaign_id JOIN ad_clients c ON c.id = k.client_id WHERE c.station_id = @Station ORDER BY f.position
            """, p, tx).ToLookup(r => r.CampaignId);
        var placements = c.Query<PlacementRow>(
            """
            SELECT a.campaign_id AS CampaignId, a.weekday AS Weekday, a.date AS Date, a.time AS Time FROM ad_placements a
            JOIN ad_campaigns k ON k.id = a.campaign_id JOIN ad_clients c ON c.id = k.client_id WHERE c.station_id = @Station ORDER BY a.id
            """, p, tx).ToLookup(r => r.CampaignId);
        tx.Commit();

        var data = new ScheduleData();
        foreach (var e in entries)
        {
            ScheduleEntry entry = e.Type switch
            {
                "break" => new AdBreak
                {
                    Parts = parts[e.Id].Select(r => new BreakPart
                    {
                        Kind = Enum.TryParse<PartKind>(r.Kind, out var k) ? k : PartKind.Jingle, Path = r.Path, Section = r.Section, OnlyWithAdverts = r.OnlyWithAdverts != 0,
                    }).ToList(),
                },
                "playlist" => new PlaylistTemplate
                {
                    Lines = lines[e.Id].Select(r => new TemplateLine
                    {
                        Source = Enum.TryParse<LineSource>(r.Source, out var s) ? s : LineSource.Folder, CategoryId = r.CategoryId ?? 0,
                        CategoryName = r.CategoryName, Kind = r.Kind, Folder = r.Folder,
                        TempoMin = r.TempoMin, TempoMax = r.TempoMax, EnergyMin = r.EnergyMin, EnergyMax = r.EnergyMax,
                    }).ToList(),
                    ArtistHours = e.ArtistHours, AlbumHours = e.AlbumHours, MaxSlowInRow = (int)e.MaxSlowInRow,
                },
                _ => new ScheduledEvent { Path = e.Path, StreamMinutes = e.StreamMinutes, MaxAgeMinutes = (int)e.MaxAgeMinutes },
            };
            entry.Id = Guid.Parse(e.Id);
            entry.Name = e.Name;
            entry.Enabled = e.Enabled != 0;
            entry.Start = Enum.TryParse<EventStart>(e.StartMode, out var start) ? start : EventStart.Soft;
            entry.MaxDelayMinutes = e.MaxDelayMinutes;
            entry.Lock = Enum.TryParse<ItemLock>(e.LockLevel, out var l) ? l : ItemLock.Free;
            entry.Slots = slots[e.Id].Select(r => new WeekTime { Day = (DayOfWeek)r.Weekday, Time = r.Time }).ToList();
            entry.Dates = dates[e.Id].Select(r => DateTime.TryParseExact(r.At, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : (DateTime?)null)
                .OfType<DateTime>().ToList();
            switch (entry)
            {
                case AdBreak b: data.Breaks.Add(b); break;
                case PlaylistTemplate t: data.Templates.Add(t); break;
                case ScheduledEvent ev: data.Events.Add(ev); break;
            }
        }
        foreach (var r in clients)
            data.Clients.Add(new AdClient
            {
                Id = Guid.Parse(r.Id), Name = r.Name, Enabled = r.Enabled != 0,
                Campaigns = campaigns[r.Id].Select(k => new AdCampaign
                {
                    Id = Guid.Parse(k.Id), Name = k.Name, Enabled = k.Enabled != 0, From = Day(k.StartsOn), To = Day(k.EndsOn),
                    Section = k.Section, Position = (int)k.Place,
                    Files = files[k.Id].Select(f => f.Path).ToList(),
                    Placements = placements[k.Id].Select(a => new AdPlacement
                    {
                        Day = a.Weekday is { } w ? (DayOfWeek)w : null, Date = Day(a.Date), Time = a.Time,
                    }).ToList(),
                }).ToList(),
            });
        data.Normalize();
        return (data, version);
    }

    /// <summary>
    /// Saves the whole schedule as a new version. With <paramref name="expected"/>: only if nobody saved since that
    /// version was read (else <see cref="ChangedException"/>). Returns the new version.
    /// </summary>
    public long Save(ScheduleData data, long? expected, string by)
    {
        data.Normalize(); // times typed as text ("*:20") into the calendar's slots
        using var c = db.Connect();
        using var tx = c.BeginTransaction();
        var current = c.QueryFirstOrDefault<VersionRow>(
            "SELECT version AS Version, updated_at AS UpdatedAt, updated_by AS UpdatedBy FROM schedule_versions WHERE station_id = @Station" + db.ForUpdate,
            new { Station }, tx);
        if (expected is { } want && (current?.Version ?? 0) != want)
            throw new ChangedException(DateTime.TryParse(current?.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime() : DateTime.Now,
                current?.UpdatedBy ?? "");

        c.Execute("DELETE FROM schedule_entries WHERE station_id = @Station", new { Station }, tx);
        c.Execute("DELETE FROM ad_clients WHERE station_id = @Station", new { Station }, tx);

        var entries = data.Events.Select((e, i) => (Entry: (ScheduleEntry)e, Type: "event", Position: i))
            .Concat(data.Breaks.Select((b, i) => (Entry: (ScheduleEntry)b, Type: "break", Position: i)))
            .Concat(data.Templates.Select((t, i) => (Entry: (ScheduleEntry)t, Type: "playlist", Position: i)));
        foreach (var (entry, type, position) in entries)
        {
            string id = entry.Id.ToString();
            var ev = entry as ScheduledEvent;
            c.Execute(
                """
                INSERT INTO schedule_entries (id, station_id, type, position, name, enabled, start_mode, max_delay_minutes, lock_level, path, stream_minutes, max_age_minutes,
                                              artist_hours, album_hours, max_slow_in_row)
                VALUES (@id, @Station, @type, @position, @name, @enabled, @start, @delay, @lock, @path, @stream, @age, @artistHours, @albumHours, @slow)
                """,
                new
                {
                    id, Station, type, position, name = entry.Name, enabled = entry.Enabled ? 1 : 0, start = entry.Start.ToString(),
                    delay = entry.MaxDelayMinutes, @lock = entry.Lock.ToString(), path = ev?.Path ?? "", stream = ev?.StreamMinutes ?? 0, age = ev?.MaxAgeMinutes ?? 0,
                    artistHours = (entry as PlaylistTemplate)?.ArtistHours ?? 0, albumHours = (entry as PlaylistTemplate)?.AlbumHours ?? 0,
                    slow = (entry as PlaylistTemplate)?.MaxSlowInRow ?? 0,
                }, tx);
            c.Execute("INSERT INTO schedule_slots (entry_id, weekday, time) VALUES (@id, @weekday, @time)",
                entry.Slots.DistinctBy(s => (s.Day, s.Time)).Select(s => new { id, weekday = (int)s.Day, time = s.Time }), tx);
            c.Execute("INSERT INTO schedule_dates (entry_id, at) VALUES (@id, @at)",
                entry.Dates.Distinct().Select(d => new { id, at = d.ToString(DateFormat, CultureInfo.InvariantCulture) }), tx);
            if (entry is AdBreak b)
                c.Execute("INSERT INTO break_parts (entry_id, position, kind, path, section, only_with_adverts) VALUES (@id, @position, @kind, @path, @section, @only)",
                    b.Parts.Select((part, i) => new { id, position = i, kind = part.Kind.ToString(), path = part.Path, section = part.Section, only = part.OnlyWithAdverts ? 1 : 0 }), tx);
            if (entry is PlaylistTemplate t)
                c.Execute("""
                    INSERT INTO playlist_lines (entry_id, position, source, category_id, category_name, kind, folder, tempo_min, tempo_max, energy_min, energy_max)
                    VALUES (@id, @position, @source, @category, @name, @kind, @folder, @tempoMin, @tempoMax, @energyMin, @energyMax)
                    """,
                    t.Lines.Select((l, i) => new
                    {
                        id, position = i, source = l.Source.ToString(), category = l.Source == LineSource.Category ? l.CategoryId : (long?)null,
                        name = l.CategoryName, kind = l.Kind, folder = l.Folder,
                        tempoMin = l.TempoMin, tempoMax = l.TempoMax, energyMin = l.EnergyMin, energyMax = l.EnergyMax,
                    }), tx);
        }
        for (int i = 0; i < data.Clients.Count; i++)
        {
            var client = data.Clients[i];
            c.Execute("INSERT INTO ad_clients (id, station_id, position, name, enabled) VALUES (@id, @Station, @position, @name, @enabled)",
                new { id = client.Id.ToString(), Station, position = i, name = client.Name, enabled = client.Enabled ? 1 : 0 }, tx);
            for (int j = 0; j < client.Campaigns.Count; j++)
            {
                var k = client.Campaigns[j];
                string kid = k.Id.ToString();
                c.Execute(
                    """
                    INSERT INTO ad_campaigns (id, client_id, position, name, enabled, starts_on, ends_on, section, place)
                    VALUES (@kid, @client, @position, @name, @enabled, @from, @to, @section, @place)
                    """,
                    new { kid, client = client.Id.ToString(), position = j, name = k.Name, enabled = k.Enabled ? 1 : 0, from = Text(k.From), to = Text(k.To), section = k.Section, place = k.Position }, tx);
                c.Execute("INSERT INTO ad_campaign_files (campaign_id, position, path) VALUES (@kid, @position, @path)",
                    k.Files.Select((f, n) => new { kid, position = n, path = f }), tx);
                c.Execute("INSERT INTO ad_placements (campaign_id, weekday, date, time) VALUES (@kid, @weekday, @date, @time)",
                    k.Placements.Select(a => new { kid, weekday = a.Date == null && a.Day is { } d ? (int?)d : null, date = Text(a.Date), time = a.Time }), tx);
            }
        }

        long version = (current?.Version ?? 0) + 1;
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        c.Execute(current == null
                ? "INSERT INTO schedule_versions (station_id, version, updated_at, updated_by) VALUES (@Station, @version, @now, @by)"
                : "UPDATE schedule_versions SET version = @version, updated_at = @now, updated_by = @by WHERE station_id = @Station",
            new { Station, version, now, by }, tx);
        tx.Commit();
        return version;
    }

    /// <summary>When each event and break last started (local time).</summary>
    public Dictionary<Guid, DateTime> LoadState()
    {
        using var c = db.Connect();
        var state = new Dictionary<Guid, DateTime>();
        foreach (var r in c.Query<StateRow>("SELECT entry_id AS EntryId, last_started AS LastStarted FROM schedule_state"))
            if (Guid.TryParse(r.EntryId, out var id) && DateTime.TryParseExact(r.LastStarted, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                state[id] = at;
        return state;
    }

    public void SaveState(IReadOnlyDictionary<Guid, DateTime> state)
    {
        using var c = db.Connect();
        using var tx = c.BeginTransaction();
        string sql = db.IsMySql
            ? "INSERT INTO schedule_state (entry_id, last_started) VALUES (@id, @at) ON DUPLICATE KEY UPDATE last_started = VALUES(last_started)"
            : "INSERT INTO schedule_state (entry_id, last_started) VALUES (@id, @at) ON CONFLICT(entry_id) DO UPDATE SET last_started = excluded.last_started";
        c.Execute(sql, state.Select(s => new { id = s.Key.ToString(), at = s.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }), tx);
        tx.Commit();
    }

    static DateTime? Day(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static string? Text(DateTime? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// Where the playout gets the schedule: the library database, with a local copy of the last one read, so the station
/// carries on when the database can't be reached (also at start-up). The first time the database has no schedule, the
/// one kept in files before (schedule.json) is put in it.
/// </summary>
sealed class ScheduleSource(Func<LibraryDb?> db)
{
    static string CacheFile => Path.Combine(AppSettings.DataFolder, "schedule-cache.json");
    readonly object _lock = new();
    bool _importTried;

    /// <summary>What to start with: the local copy, else the old files, else nothing.</summary>
    public static ScheduleData LoadLocal() => ScheduleData.ReadFile(CacheFile) ?? ScheduleData.LoadLegacy() ?? new ScheduleData();

    /// <summary>The schedule's version in the database (importing the old files the first time); null without the database.</summary>
    public long? Version()
    {
        if (db() is not { } library) return null;
        var store = new ScheduleStore(library);
        long version = store.Version();
        if (version == 0 && !_importTried)
        {
            _importTried = true;
            version = Import(store);
        }
        return version;
    }

    /// <summary>The schedule from the database, with when each started; written to the local copy. Null without the database.</summary>
    public ScheduleData? Load()
    {
        if (db() is not { } library) return null;
        var store = new ScheduleStore(library);
        var (data, _) = store.Load();
        data.LastStarted = store.LoadState();
        lock (_lock) data.WriteFile(CacheFile);
        return data;
    }

    /// <summary>When each started: the local copy straight away, the database if it can be reached.</summary>
    public void SaveState(ScheduleData data)
    {
        lock (_lock)
        {
            data.WriteFile(CacheFile);
            if (db() is not { } library) return;
            try { new ScheduleStore(library).SaveState(data.LastStarted); }
            catch (Exception ex) { AppLog.Write($"Schedule state not saved in the database: {ex.Message}"); }
        }
    }

    /// <summary>The old files (or the local copy) into an empty database; 0 if there is nothing to import.</summary>
    static long Import(ScheduleStore store)
    {
        var legacy = ScheduleData.LoadLegacy() ?? ScheduleData.ReadFile(CacheFile);
        if (legacy == null || (legacy.Events.Count + legacy.Breaks.Count + legacy.Templates.Count + legacy.Clients.Count) == 0) return 0;
        long version;
        try { version = store.Save(legacy, 0, L.T("import")); }
        catch (ScheduleStore.ChangedException) { return store.Version(); } // another computer imported it first
        store.SaveState(legacy.LastStarted);
        ScheduleData.RetireLegacy();
        AppLog.Write($"Schedule imported into the database from {ScheduleData.LegacyFile}");
        return version;
    }
}
