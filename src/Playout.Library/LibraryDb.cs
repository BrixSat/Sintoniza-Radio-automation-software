using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Playout.Library;

public sealed class LibraryCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>music | jingle | commercial | other</summary>
    public string Kind { get; set; } = "music";
    public string? Color { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LibraryTrack
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public long FileSize { get; set; }
    public string? FileModified { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public int? Year { get; set; }
    public string? Isrc { get; set; }
    public double Duration { get; set; }
    public long? CategoryId { get; set; }
    public int? Tempo { get; set; }
    public string? Voice { get; set; }
    public string? Language { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Missing { get; set; }
}

/// <summary>Which tracks to list: all, those without a category, those whose file is missing, or one category.</summary>
public readonly record struct CategoryFilter(long? Id = null, bool Uncategorised = false, bool MissingFiles = false)
{
    public static readonly CategoryFilter All = new();
}

/// <summary>A page of search results: the first <see cref="Tracks"/>, out of <see cref="Total"/> matching.</summary>
public sealed record TrackPage(IReadOnlyList<LibraryTrack> Tracks, int Total);

public sealed record LibraryStats(int Tracks, int Missing, int Uncategorised, double Hours);

/// <summary>Attributes to set on several tracks at once; null = leave as it is.</summary>
public sealed record TrackAttributes(int? Tempo = null, string? Voice = null, string? Language = null, bool? Enabled = null,
    bool ClearTempo = false, bool ClearVoice = false, bool ClearLanguage = false);

/// <summary>
/// The music library, in SQLite (a file) or MySQL / MariaDB (a server several studios share). Every call opens its
/// own pooled connection, so it can be used from any thread, and another program (radio-import, or your own) can
/// write to it at the same time. The tables are documented in docs/library-database.md.
/// </summary>
public sealed class LibraryDb
{
    public const int SchemaVersion = 1;

    /// <summary>Longest path the library takes (the MySQL unique index limit); longer ones are skipped by scans.</summary>
    public const int MaxPathLength = 760;

    const string TrackColumns = """
        id AS Id, path AS Path, file_size AS FileSize, file_modified AS FileModified, title AS Title, artist AS Artist,
        album AS Album, genre AS Genre, year AS Year, isrc AS Isrc, duration AS Duration, category_id AS CategoryId,
        tempo AS Tempo, voice AS Voice, language AS Language, enabled AS Enabled, missing AS Missing
        """;

    readonly string _connectionString;
    readonly bool _mySql;

    /// <summary>Where the library is, for messages: the file, or user@server/database.</summary>
    public string Location { get; }

    /// <summary>Where the app and radio-import keep the library unless told otherwise.</summary>
    public static string DefaultFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "RadioPlayout", "library.db");

    LibraryDb(LibrarySettings settings)
    {
        _mySql = settings.IsMySql;
        Location = settings.Describe();
        _connectionString = _mySql
            ? new MySqlConnectionStringBuilder
            {
                Server = settings.Server,
                Port = (uint)settings.Port,
                Database = settings.Database,
                UserID = settings.User,
                Password = settings.Password,
                CharacterSet = "utf8mb4",
                ConnectionTimeout = 10,
                DefaultCommandTimeout = 60,
            }.ToString()
            : new SqliteConnectionStringBuilder
            {
                DataSource = settings.SqliteFile,
                ForeignKeys = true,
                DefaultTimeout = 30, // waits this long for a writer in another program
                Pooling = true,
            }.ToString();
    }

    /// <summary>Opens an SQLite library file.</summary>
    public static LibraryDb Open(string file) => Open(new LibrarySettings { File = file });

    /// <summary>
    /// Opens the library, creating its tables (with the starter categories) or upgrading them as needed.
    /// For MySQL the database and user must exist; the tables are made here.
    /// </summary>
    public static LibraryDb Open(LibrarySettings settings)
    {
        if (!settings.IsMySql)
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(settings.SqliteFile))!);
        var db = new LibraryDb(settings);
        using var c = db.Connect();
        if (!db._mySql) c.Execute("PRAGMA journal_mode = WAL;");
        bool hasSchema = db._mySql
            ? c.ExecuteScalar<long>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'schema_info'") > 0
            : c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_info'") > 0;
        int version = hasSchema
            ? int.Parse(c.ExecuteScalar<string>("SELECT value FROM schema_info WHERE name = 'version'") ?? "0", CultureInfo.InvariantCulture)
            : 0;
        if (version > SchemaVersion)
            throw new InvalidDataException($"The library was made by a newer version (schema {version}); update the app.");
        for (int v = version + 1; v <= SchemaVersion; v++)
        {
            // (MySQL commits table changes as it goes; SQLite does the whole step or nothing.)
            using var tx = c.BeginTransaction();
            c.Execute(Script($"{(db._mySql ? "mysql" : "sqlite")}-{v}.sql"), transaction: tx);
            tx.Commit();
        }
        db.FillSearchText(c);
        return db;
    }

    static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing schema script {name}");
        return new StreamReader(stream).ReadToEnd();
    }

    internal DbConnection Connect()
    {
        DbConnection c = _mySql ? new MySqlConnection(_connectionString) : new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    /// <summary>
    /// SQL that compares a path column with a parameter the way this computer's file system does: exactly on Linux,
    /// ignoring case on Windows and macOS (where D:\Music\a.mp3 and d:\music\A.MP3 are the same file).
    /// </summary>
    internal string PathIs(string column, string parameter = "@path") =>
        OperatingSystem.IsLinux() ? $"{column} = {parameter}"
        : _mySql ? $"LOWER({column}) = LOWER({parameter})"
        : $"{column} = {parameter} COLLATE NOCASE";

    /// <summary>A column sorted without regard to case (MySQL's usual collations already are).</summary>
    string NoCase(string column) => _mySql ? column : $"{column} COLLATE NOCASE";

    // ───────────────────────── Search ─────────────────────────

    /// <summary>
    /// Tracks whose artist, title, album, genre or file name contain every word of <paramref name="text"/> (case and
    /// accents ignored), sorted by artist and title; at most <paramref name="limit"/>, plus how many match in all.
    /// </summary>
    public TrackPage Search(string? text, CategoryFilter filter, int limit = 1000)
    {
        var where = new List<string>();
        var args = new DynamicParameters();
        var words = Normalize(text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            // '!' escapes: a backslash means different things in MySQL and SQLite strings.
            where.Add($"search LIKE @w{i} ESCAPE '!'");
            args.Add($"w{i}", "%" + words[i].Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%");
        }
        if (filter.MissingFiles) where.Add("missing = 1");
        else
        {
            where.Add("missing = 0");
            if (filter.Uncategorised) where.Add("category_id IS NULL");
            else if (filter.Id is { } id) { where.Add("category_id = @category"); args.Add("category", id); }
        }
        string condition = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        args.Add("limit", limit);

        using var c = Connect();
        int total = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM tracks {condition}", args);
        var tracks = c.Query<LibraryTrack>(
            $"SELECT {TrackColumns} FROM tracks {condition} ORDER BY {NoCase("artist")}, {NoCase("title")} LIMIT @limit", args).ToList();
        return new TrackPage(tracks, total);
    }

    public LibraryTrack? GetByPath(string path)
    {
        using var c = Connect();
        return c.QueryFirstOrDefault<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE {PathIs("path")} ORDER BY id", new { path });
    }

    public LibraryStats Stats()
    {
        using var c = Connect();
        var (tracks, missing, uncategorised, seconds) = c.QuerySingle<(long, long, long, double)>("""
            SELECT COUNT(*),
                   COALESCE(SUM(missing), 0),
                   COALESCE(SUM(CASE WHEN category_id IS NULL AND missing = 0 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(duration), 0.0)
            FROM tracks
            """);
        return new LibraryStats((int)tracks, (int)missing, (int)uncategorised, seconds / 3600);
    }

    // ───────────────────────── Categories ─────────────────────────

    public IReadOnlyList<LibraryCategory> Categories()
    {
        using var c = Connect();
        return c.Query<LibraryCategory>(
            "SELECT id AS Id, name AS Name, kind AS Kind, color AS Color, sort_order AS SortOrder FROM categories ORDER BY sort_order, name")
            .ToList();
    }

    public long AddCategory(string name, string kind, string? color)
    {
        using var c = Connect();
        int order = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), 0) + 1 FROM categories");
        return c.ExecuteScalar<long>($"""
            INSERT INTO categories (name, kind, color, sort_order) VALUES (@name, @kind, @color, @order);
            SELECT {(_mySql ? "LAST_INSERT_ID()" : "last_insert_rowid()")};
            """, new { name, kind, color, order });
    }

    public void UpdateCategory(LibraryCategory category)
    {
        using var c = Connect();
        c.Execute("UPDATE categories SET name = @Name, kind = @Kind, color = @Color, sort_order = @SortOrder WHERE id = @Id", category);
    }

    /// <summary>Deletes a category; its tracks become uncategorised.</summary>
    public void DeleteCategory(long id)
    {
        using var c = Connect();
        c.Execute("DELETE FROM categories WHERE id = @id", new { id });
    }

    // ───────────────────────── Editing ─────────────────────────

    public void SetCategory(IEnumerable<long> trackIds, long? categoryId)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute("UPDATE tracks SET category_id = @categoryId, updated_at = @now WHERE id = @id",
            trackIds.Select(id => new { id, categoryId, now = Now() }), tx);
        tx.Commit();
    }

    public void SetAttributes(IEnumerable<long> trackIds, TrackAttributes a)
    {
        var sets = new List<string> { "updated_at = @now" };
        if (a.ClearTempo) sets.Add("tempo = NULL"); else if (a.Tempo != null) sets.Add("tempo = @Tempo");
        if (a.ClearVoice) sets.Add("voice = NULL"); else if (a.Voice != null) sets.Add("voice = @Voice");
        if (a.ClearLanguage) sets.Add("language = NULL"); else if (a.Language != null) sets.Add("language = @Language");
        if (a.Enabled != null) sets.Add("enabled = @Enabled");
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute($"UPDATE tracks SET {string.Join(", ", sets)} WHERE id = @id",
            trackIds.Select(id => new { id, now = Now(), a.Tempo, a.Voice, a.Language, a.Enabled }), tx);
        tx.Commit();
    }

    /// <summary>Re-reads one file's tags into its track (after its tags were edited); adds it if it isn't in the library.</summary>
    public void Refresh(string path) => LibraryScanner.ScanFile(this, path);

    // ───────────────────────── History ─────────────────────────

    /// <summary>Records an item going on air (linked to its track when the file is in the library).</summary>
    public void RecordPlay(string path, string artist, string title, DateTime startedUtc, string source)
    {
        using var c = Connect();
        long? trackId = c.ExecuteScalar<long?>($"SELECT id FROM tracks WHERE {PathIs("path")} ORDER BY id LIMIT 1", new { path });
        c.Execute("""
            INSERT INTO plays (track_id, path, artist, title, started_at, source)
            VALUES (@trackId, @path, @artist, @title, @started, @source)
            """, new { trackId, path, artist, title, started = Time(startedUtc), source });
    }

    /// <summary>When each of these files last went on air (files never played are left out).</summary>
    public Dictionary<long, DateTime> LastPlayed(IEnumerable<long> trackIds)
    {
        using var c = Connect();
        return c.Query<(long Id, string At)>(
                "SELECT track_id, MAX(started_at) FROM plays WHERE track_id IN @ids GROUP BY track_id", new { ids = trackIds.ToArray() })
            .ToDictionary(r => r.Id, r => DateTime.Parse(r.At, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
    }

    // ───────────────────────── Helpers ─────────────────────────

    internal static string Now() => Time(DateTime.UtcNow);

    internal static string Time(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>What search matches against: lower case, accents removed ("Canção" → "cancao").</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static string SearchText(string path, string artist, string title, string album, string genre) =>
        Normalize($"{artist} {title} {album} {genre} {System.IO.Path.GetFileNameWithoutExtension(path)}");

    /// <summary>Tracks inserted by another program without search text get it here.</summary>
    void FillSearchText(DbConnection c)
    {
        var rows = c.Query<(long Id, string Path, string Artist, string Title, string Album, string Genre)>(
            "SELECT id, path, artist, title, album, genre FROM tracks WHERE search = ''").ToList();
        if (rows.Count == 0) return;
        using var tx = c.BeginTransaction();
        c.Execute("UPDATE tracks SET search = @search WHERE id = @id",
            rows.Select(r => new { id = r.Id, search = SearchText(r.Path, r.Artist, r.Title, r.Album, r.Genre) }), tx);
        tx.Commit();
    }
}
