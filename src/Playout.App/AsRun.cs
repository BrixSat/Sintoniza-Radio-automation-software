using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>One item that went on air, as the as-run log has it. Times are local.</summary>
public sealed record AsRunEntry(
    DateTime Start,
    DateTime? End,
    string Type,
    string Source,
    string Event,
    string Advertiser,
    string Artist,
    string Title,
    string Album,
    string Composer,
    string Label,
    string Isrc,
    string Year,
    string File)
{
    /// <summary>How long it was on air; null when the app closed or crashed while it was (end unknown).</summary>
    public double? Seconds => End is { } end ? Math.Max(0, (end - Start).TotalSeconds) : null;
}

/// <summary>
/// The as-run log: every item that went on air on this computer (playlist and carts), with when it started and
/// ended. Each goes into the library database (the as_run table, what the reports are made from) and into one CSV file
/// a day in the as-run folder, kept as this computer's own record: what the database missed while it couldn't be
/// reached is put in later from there.
/// </summary>
sealed class AsRunLog
{
    /// <summary>The library database, when it is open (set by the main window).</summary>
    public Func<LibraryDb?> Database { get; set; } = () => null;
    bool _historySynced;
    DateTime _nextSync = DateTime.MinValue;

    public static readonly string[] Header =
        ["Date", "Start", "End", "Seconds", "Type", "Source", "Event", "Advertiser", "Artist", "Title", "Album", "Composer", "Label", "ISRC", "Year", "File"];

    readonly string _folder;
    readonly object _lock = new();
    Task _writes = Task.CompletedTask;
    (Track Track, DateTime Started, string Source)? _onAir;

    public AsRunLog(string folder)
    {
        _folder = folder;
        // What was on air when the app last stopped without closing (a crash, a power cut): logged with no end.
        Queue(() =>
        {
            if (ReadPending() is { } pending) Append(pending);
            DeletePending();
        });
    }

    public static string DefaultFolder => Path.Combine(AppSettings.DataFolder, "as-run");

    string PendingFile => Path.Combine(_folder, "on-air.json");

    /// <summary>An item went on air (UTC): remembered, so it is logged even if the app goes down while it plays.</summary>
    public void Started(Track track, DateTime startedUtc, string source = "playlist")
    {
        lock (_lock) _onAir = (track, startedUtc, source);
        Queue(() =>
        {
            var entry = Entry(track, startedUtc.ToLocalTime(), null, source);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(PendingFile, JsonSerializer.Serialize(entry));
        });
    }

    /// <summary>An item left the air (UTC times): written to the day's file.</summary>
    public void Ended(Track track, DateTime startedUtc, DateTime endedUtc, string source = "playlist")
    {
        lock (_lock)
            if (_onAir?.Track.Id == track.Id) _onAir = null;
        // The engine reports an item ending before the next one starts, so the remembered one is this one.
        Queue(() =>
        {
            Append(Entry(track, startedUtc.ToLocalTime(), endedUtc.ToLocalTime(), source));
            DeletePending();
        });
    }

    /// <summary>Closing the app: what is on air ends now; waits (a moment) for everything to be written.</summary>
    public void Close()
    {
        (Track Track, DateTime Started, string Source)? onAir;
        lock (_lock) onAir = _onAir;
        if (onAir is { } o) Ended(o.Track, o.Started, DateTime.UtcNow, o.Source);
        Flush(TimeSpan.FromSeconds(3));
    }

    /// <summary>A cart was fired: logged straight away, as playing to its end (UTC start).</summary>
    public void Cart(string path, DateTime startedUtc, double seconds)
    {
        Queue(() =>
        {
            TrackTags? tags = null;
            try { tags = Tags.Read(path); } catch { }
            string title = string.IsNullOrWhiteSpace(tags?.Title) ? Path.GetFileNameWithoutExtension(path) : tags.Title.Trim();
            var track = new Track(Guid.NewGuid(), path, tags?.Artist.Trim() ?? "", title, seconds, TrackClassifier.Classify(path));
            Append(Entry(track, startedUtc.ToLocalTime(), startedUtc.ToLocalTime().AddSeconds(seconds), "cart"));
        });
    }

    /// <summary>Waits for what is queued to be written (closing the app).</summary>
    public void Flush(TimeSpan timeout)
    {
        Task writes;
        lock (_lock) writes = _writes;
        writes.Wait(timeout);
    }

    void Queue(Action write)
    {
        lock (_lock)
            _writes = _writes.ContinueWith(_ =>
            {
                try { write(); }
                catch (Exception ex) { AppLog.Write($"As-run log not written: {ex.Message}"); }
            }, TaskScheduler.Default);
    }

    static AsRunEntry Entry(Track track, DateTime start, DateTime? end, string source)
    {
        var details = track.IsStream ? new TrackDetails() : Tags.ReadDetails(track.Path);
        return new AsRunEntry(start, end, TypeName(track.Kind), source, track.Event ?? "", track.Advertiser ?? "",
            track.Artist, track.Title, details.Album, details.Composer, details.Label, details.Isrc,
            details.Year > 0 ? details.Year.ToString(CultureInfo.InvariantCulture) : "", track.Path);
    }

    public static string TypeName(TrackKind kind) => kind switch
    {
        TrackKind.Jingle => "Jingle",
        TrackKind.Commercial => "Advert",
        TrackKind.Stream => "Stream",
        TrackKind.Programme => "Programme",
        _ => "Music",
    };

    /// <summary>
    /// Puts what the database is missing from the CSV files into it: the whole history the first time the database is
    /// there, then the last two days every ten minutes (the database ignores what it has already).
    /// </summary>
    public void Sync(bool now = false)
    {
        if (Database() is not { } db || (!now && DateTime.UtcNow < _nextSync)) return;
        _nextSync = DateTime.UtcNow.AddMinutes(10);
        Queue(() =>
        {
            DateTime from = _historySynced ? DateTime.Today.AddDays(-2) : FirstDay() ?? DateTime.Today;
            int added = AsRunDb.Add(db, Read(_folder, from, DateTime.Today));
            _historySynced = true;
            if (added > 0) AppLog.Write($"As-run: {added} put in the database from the files");
        });
    }

    DateTime? FirstDay()
    {
        if (!Directory.Exists(_folder)) return null;
        return Directory.GetFiles(_folder, "*.csv").Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .Where(d => d != null).DefaultIfEmpty(null).Min();
    }

    void Append(AsRunEntry e)
    {
        if (Database() is { } db)
            try { AsRunDb.Add(db, [e]); }
            catch (Exception ex) { AppLog.Write($"As-run not put in the database (it will be later): {ex.Message}"); }
        Directory.CreateDirectory(_folder);
        string file = Path.Combine(_folder, $"{e.Start:yyyy-MM-dd}.csv");
        bool header = !File.Exists(file);
        var line = new StringBuilder();
        if (header) line.Append(Csv.Line(Header, ',')).Append('\n');
        line.Append(Csv.Line(
        [
            e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            e.Start.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            e.End?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            e.Seconds is { } s ? Math.Round(s).ToString(CultureInfo.InvariantCulture) : "",
            e.Type, e.Source, e.Event, e.Advertiser, e.Artist, e.Title, e.Album, e.Composer, e.Label, e.Isrc, e.Year, e.File,
        ], ',')).Append('\n');
        File.AppendAllText(file, line.ToString(), new UTF8Encoding(false));
    }

    AsRunEntry? ReadPending()
    {
        try { return File.Exists(PendingFile) ? JsonSerializer.Deserialize<AsRunEntry>(File.ReadAllText(PendingFile)) : null; }
        catch { return null; }
    }

    void DeletePending()
    {
        try { File.Delete(PendingFile); } catch { }
    }

    /// <summary>Everything that went on air between two days (inclusive, local dates), in time order.</summary>
    public static List<AsRunEntry> Read(string folder, DateTime from, DateTime to)
    {
        var entries = new List<AsRunEntry>();
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            string file = Path.Combine(folder, $"{day:yyyy-MM-dd}.csv");
            if (!File.Exists(file)) continue;
            string text;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            }
            catch
            {
                continue;
            }
            foreach (var row in Csv.Parse(text).Skip(1))
                if (Parse(row) is { } e) entries.Add(e);
        }
        return entries.OrderBy(e => e.Start).ToList();
    }

    static AsRunEntry? Parse(IReadOnlyList<string> row)
    {
        if (row.Count < Header.Length) return null;
        var f = row.Select(Csv.Unguard).ToList();
        if (!DateTime.TryParseExact($"{f[0]} {f[1]}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return null;
        DateTime? end = null;
        if (TimeSpan.TryParseExact(f[2], @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var endTime))
        {
            end = start.Date + endTime;
            if (end < start) end = end.Value.AddDays(1); // past midnight
        }
        return new AsRunEntry(start, end, f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
    }
}

/// <summary>Comma (or semicolon) separated values, quoted where needed (RFC 4180).</summary>
static class Csv
{
    public static string Line(IEnumerable<string> fields, char separator) =>
        string.Join(separator, fields.Select(f => Quote(f, separator)));

    static string Quote(string field, char separator)
    {
        field ??= "";
        // A leading = + - @ would make a spreadsheet run it as a formula.
        if (field.Length > 0 && "=+-@".Contains(field[0]) && !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            field = "'" + field;
        return field.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>The value written with <see cref="Line"/>, as it was (the formula guard removed).</summary>
    public static string Unguard(string field) =>
        field.Length > 1 && field[0] == '\'' && "=+-@".Contains(field[1]) ? field[1..] : field;
}

/// <summary>The as-run log in the library database (as_run), for the station.</summary>
static class AsRunDb
{
    const string Columns = "station_id, started_at, ended_at, type, source, event, advertiser, artist, title, album, composer, label, isrc, year, file";

    /// <summary>Adds entries, leaving out those already there (same start and file); returns how many were new.</summary>
    public static int Add(LibraryDb db, IEnumerable<AsRunEntry> entries)
    {
        var rows = entries.Select(e => new
        {
            station = ScheduleStore.Station,
            start = e.Start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            end = e.End?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            e.Type, e.Source, e.Event, e.Advertiser, e.Artist, e.Title, e.Album, e.Composer, e.Label, e.Isrc, e.Year,
            File = e.File.Length > 760 ? e.File[..760] : e.File,
        }).ToList();
        if (rows.Count == 0) return 0;
        using var c = db.Connect();
        using var tx = c.BeginTransaction();
        int added = c.Execute(
            $"INSERT {(db.IsMySql ? "IGNORE" : "OR IGNORE")} INTO as_run ({Columns}) VALUES (@station, @start, @end, @Type, @Source, @Event, @Advertiser, @Artist, @Title, @Album, @Composer, @Label, @Isrc, @Year, @File)",
            rows, tx);
        tx.Commit();
        return added;
    }

    sealed class Row
    {
        public string StartedAt { get; set; } = "";
        public string? EndedAt { get; set; }
        public string Type { get; set; } = "";
        public string Source { get; set; } = "";
        public string Event { get; set; } = "";
        public string Advertiser { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Album { get; set; } = "";
        public string Composer { get; set; } = "";
        public string Label { get; set; } = "";
        public string Isrc { get; set; } = "";
        public string Year { get; set; } = "";
        public string File { get; set; } = "";
    }

    /// <summary>Everything that went on air between two days (inclusive, local dates), in time order.</summary>
    public static List<AsRunEntry> Read(LibraryDb db, DateTime from, DateTime to)
    {
        using var c = db.Connect();
        var rows = c.Query<Row>(
            """
            SELECT started_at AS StartedAt, ended_at AS EndedAt, type AS Type, source AS Source, event AS Event, advertiser AS Advertiser,
                   artist AS Artist, title AS Title, album AS Album, composer AS Composer, label AS Label, isrc AS Isrc, year AS Year, file AS File
            FROM as_run WHERE station_id = @station AND started_at >= @from AND started_at < @to ORDER BY started_at
            """,
            new
            {
                station = ScheduleStore.Station,
                from = from.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = to.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            });
        var list = new List<AsRunEntry>();
        foreach (var r in rows)
        {
            if (!DateTime.TryParseExact(r.StartedAt, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) continue;
            DateTime? end = DateTime.TryParseExact(r.EndedAt, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : null;
            list.Add(new AsRunEntry(start, end, r.Type, r.Source, r.Event, r.Advertiser, r.Artist, r.Title, r.Album, r.Composer, r.Label, r.Isrc, r.Year, r.File));
        }
        return list;
    }

    /// <summary>
    /// For the reports: the database's entries, with this computer's files for what the database doesn't have yet (or
    /// only the files, without the database).
    /// </summary>
    public static List<AsRunEntry> ReadAll(LibraryDb? db, string folder, DateTime from, DateTime to)
    {
        var files = AsRunLog.Read(folder, from, to);
        if (db == null) return files;
        List<AsRunEntry> stored;
        try { stored = Read(db, from, to); }
        catch (Exception ex)
        {
            AppLog.Write($"As-run not read from the database, the files are used: {ex.Message}");
            return files;
        }
        var known = stored.Select(Key).ToHashSet();
        return stored.Concat(files.Where(e => !known.Contains(Key(e)))).OrderBy(e => e.Start).ToList();

        static (string, string) Key(AsRunEntry e) => (e.Start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), e.File.Length > 760 ? e.File[..760] : e.File);
    }
}
