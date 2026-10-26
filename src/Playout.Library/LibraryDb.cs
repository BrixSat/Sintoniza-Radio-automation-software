using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Playout.Engine;

namespace Playout.Library;

public sealed class LibraryCategory
{
    public long Id { get; set; }
    /// <summary>The category this one is under (Music for Music › 80s); null at the top.</summary>
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>music | jingle | commercial | other</summary>
    public string Kind { get; set; } = "music";
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    /// <summary>One of the eight fixed top-level categories (<see cref="FixedCategories"/>): it can't be deleted, renamed or moved.</summary>
    public string? Role { get; set; }
    public bool IsFixed => Role != null;
    /// <summary>ERC defaults for its songs (and the categories under it); null: as the category above says.</summary>
    public bool? ErcPortuguese { get; set; }
    public string? ErcLanguage { get; set; }
    public string? ErcNationality { get; set; }
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
    /// <summary>The song's own category.</summary>
    public long? CategoryId { get; set; }
    /// <summary>The other categories it is in too (track_categories); filled by <see cref="LibraryDb.Search"/>.</summary>
    public List<long> AlsoIn { get; set; } = new();
    /// <summary>1 slow … 5 fast; null: not set.</summary>
    public int? Tempo { get; set; }
    /// <summary>1 calm … 5 lively; null: not set.</summary>
    public int? Energy { get; set; }
    public string? Voice { get; set; }
    public string? Language { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Missing { get; set; }
    /// <summary>ERC: Portuguese music (null: as its categories say).</summary>
    public bool? ErcPortuguese { get; set; }
    /// <summary>ERC: the performer's country, as the ERC's code (null: as its categories say).</summary>
    public string? ErcNationality { get; set; }
    /// <summary>ERC: first edition, yyyy-mm-dd, yyyy-mm or yyyy (null: the year tag).</summary>
    public string? ErcRelease { get; set; }

    public string? PlayFrom { get; set; }
    public string? PlayUntil { get; set; }
    public string? PlayDays { get; set; }
    public string? PlayHours { get; set; }
    public int? MaxDay { get; set; }
    public int? MaxWeek { get; set; }

    /// <summary>When it may be picked automatically (null: any time).</summary>
    public SongRestrictions? Restrictions => SongRestrictions.Make(PlayFrom, PlayUntil, PlayDays, PlayHours, MaxDay, MaxWeek);
}

/// <summary>Which tracks to list: all, those without a category, those whose file is missing, or one category.</summary>
public readonly record struct CategoryFilter(long? Id = null, bool Uncategorised = false, bool MissingFiles = false)
{
    public static readonly CategoryFilter All = new();
}

/// <summary>A page of search results: the first <see cref="Tracks"/>, out of <see cref="Total"/> matching.</summary>
public sealed record TrackPage(IReadOnlyList<LibraryTrack> Tracks, int Total);

/// <summary>A song fill music may pick: <see cref="LastPlayed"/> is UTC, null if it never went on air.</summary>
/// <param name="Duration">Seconds; with the cue points (null: not set) it tells how long it plays.</param>
/// <param name="Tempo">1 slow … 5 fast; <paramref name="Energy"/> 1 calm … 5 lively (null: not set).</param>
/// <param name="Rules">When it may be picked (null: any time).</param>
public sealed record RotationTrack(long Id, string Path, string Artist, DateTime? LastPlayed, string Title = "", double Duration = 0,
    double? CueIn = null, double? CueOut = null, double? Mix = null, string Album = "", int? Tempo = null, int? Energy = null,
    SongRestrictions? Rules = null);

/// <summary>An item that went on air (UTC); <see cref="TrackId"/> is null for files not in the library.</summary>
public sealed record RecentPlay(long? TrackId, string Artist, DateTime At, string Album = "", int? Tempo = null);

public sealed record LibraryStats(int Tracks, int Missing, int Uncategorised, double Hours);

/// <summary>Attributes to set on several tracks at once; null = leave as it is.</summary>
public sealed record TrackAttributes(int? Tempo = null, string? Voice = null, string? Language = null, bool? Enabled = null,
    bool ClearTempo = false, bool ClearVoice = false, bool ClearLanguage = false, int? Energy = null, bool ClearEnergy = false,
    bool? ErcPortuguese = null, bool ClearErcPortuguese = false, string? ErcNationality = null, bool ClearErcNationality = false,
    string? ErcRelease = null, bool ClearErcRelease = false);

/// <summary>
/// The music library, in SQLite (a file) or MySQL / MariaDB (a server several studios share). Every call opens its
/// own pooled connection, so it can be used from any thread, and another program (radio-import, or your own) can
/// write to it at the same time. The tables are documented in docs/library-database.md.
/// </summary>
public sealed partial class LibraryDb
{
    public const int SchemaVersion = 15;

    /// <summary>Longest path the library takes (the MySQL unique index limit); longer ones are skipped by scans.</summary>
    public const int MaxPathLength = 760;

    const string TrackColumns = """
        id AS Id, path AS Path, file_size AS FileSize, file_modified AS FileModified, title AS Title, artist AS Artist,
        album AS Album, genre AS Genre, year AS Year, isrc AS Isrc, duration AS Duration, category_id AS CategoryId,
        tempo AS Tempo, energy AS Energy, voice AS Voice, language AS Language, enabled AS Enabled, missing AS Missing,
        erc_portuguese AS ErcPortuguese, erc_nationality AS ErcNationality, erc_release AS ErcRelease,
        play_from AS PlayFrom, play_until AS PlayUntil, play_days AS PlayDays, play_hours AS PlayHours, max_day AS MaxDay, max_week AS MaxWeek
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
                GuidFormat = MySqlGuidFormat.None, // ids in CHAR(36) columns are read as text
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
            string script = Script($"{(db._mySql ? "mysql" : "sqlite")}-{v}.sql");
            if (db._mySql)
            {
                // MySQL commits table changes as it goes, so a step that stopped halfway is finished here: one
                // statement at a time, skipping columns and indexes that are already there.
                foreach (var statement in Statements(script))
                {
                    try { c.Execute(statement); }
                    catch (MySqlException ex) when (ex.ErrorCode is MySqlErrorCode.DuplicateFieldName or MySqlErrorCode.DuplicateKeyName
                                                        or MySqlErrorCode.CannotDropFieldOrKey) { }
                }
            }
            else
            {
                // SQLite does the whole step or nothing. Foreign keys off meanwhile: a step may make a table again
                // (drop and rename), which must not empty the columns that point at it.
                c.Execute("PRAGMA foreign_keys = OFF;");
                try
                {
                    using var tx = c.BeginTransaction();
                    c.Execute(script, transaction: tx);
                    tx.Commit();
                }
                finally
                {
                    c.Execute("PRAGMA foreign_keys = ON;");
                }
            }
        }
        if (version < 4) DropEmptyOldTables(c, db._mySql);
        FixedCategories.Ensure(c, Normalize, db._mySql);
        new UserStore(db).EnsureDefaults();
        db.FillSearchText(c);
        return db;
    }

    /// <summary>
    /// The tables of the earlier design (daily logs made from clocks, its own advertisers and campaigns), replaced by
    /// the schedule tables of version 4: removed if nothing was ever put in them, else left alone.
    /// </summary>
    static void DropEmptyOldTables(DbConnection c, bool mySql)
    {
        string[] old = ["log_items", "logs", "bookings", "campaign_spots", "campaigns", "advertisers", "rotation_rules",
                        "clock_overrides", "clock_grid", "clock_slots", "clocks", "schedule_settings"];
        foreach (var table in old)
        {
            bool exists = mySql
                ? c.ExecuteScalar<long>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table", new { table }) > 0
                : c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table", new { table }) > 0;
            if (!exists || c.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}") > 0) continue;
            try { c.Execute($"DROP TABLE {table}"); }
            catch (DbException) { /* still referenced by a table that has rows: left */ }
        }
    }

    static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing schema script {name}");
        return new StreamReader(stream).ReadToEnd();
    }

    /// <summary>The statements of a script: split at semicolons that end a line, comment lines left out.</summary>
    static IEnumerable<string> Statements(string script)
    {
        var current = new StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            string text = line.TrimEnd('\r');
            if (text.TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
            current.AppendLine(text);
            if (!text.TrimEnd().EndsWith(';')) continue;
            if (current.ToString().Trim() is { Length: > 1 } statement) yield return statement;
            current.Clear();
        }
        if (current.ToString().Trim() is { Length: > 0 } rest) yield return rest;
    }

    public bool IsMySql => _mySql;

    /// <summary>A connection to the library, open (for the app's own tables: schedule, as-run).</summary>
    public DbConnection Connect()
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
            if (filter.Uncategorised) where.Add($"category_id IS NULL AND {NotAlsoIn}");
            else if (filter.Id is { } id) { where.Add(InCategories); args.Add("categories", CategoryTree.WithDescendants(Categories(), [id])); }
        }
        string condition = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        args.Add("limit", limit);

        using var c = Connect();
        int total = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM tracks {condition}", args);
        var tracks = c.Query<LibraryTrack>(
            $"SELECT {TrackColumns} FROM tracks {condition} ORDER BY {NoCase("artist")}, {NoCase("title")} LIMIT @limit", args).ToList();
        if (tracks.Count > 0)
        {
            var byId = tracks.ToDictionary(t => t.Id);
            foreach (var (track, category) in c.Query<(long, long)>(
                         "SELECT track_id, category_id FROM track_categories WHERE track_id IN @ids", new { ids = byId.Keys.ToArray() }))
                byId[track].AlsoIn.Add(category);
        }
        return new TrackPage(tracks, total);
    }

    /// <summary>Tracks in one of @categories: their own category, or also in it.</summary>
    const string InCategories =
        "(category_id IN @categories OR id IN (SELECT track_id FROM track_categories WHERE category_id IN @categories))";

    /// <summary>Tracks in no other category either.</summary>
    const string NotAlsoIn = "NOT EXISTS (SELECT 1 FROM track_categories tc WHERE tc.track_id = tracks.id)";

    /// <summary>The tracks with these ids (those that are still there).</summary>
    public List<LibraryTrack> GetByIds(IEnumerable<long> ids)
    {
        using var c = Connect();
        var list = new List<LibraryTrack>();
        foreach (var chunk in ids.Distinct().Chunk(400))
            list.AddRange(c.Query<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE id IN @ids", new { ids = chunk }));
        return list;
    }

    public LibraryTrack? GetByPath(string path)
    {
        using var c = Connect();
        return c.QueryFirstOrDefault<LibraryTrack>($"SELECT {TrackColumns} FROM tracks WHERE {PathIs("path")} ORDER BY id", new { path });
    }

    /// <summary>
    /// A fingerprint of the categories and of the songs (how many, and the latest change): when it differs from the
    /// last one, another program or computer changed the library.
    /// </summary>
    public string ChangeStamp()
    {
        var categories = Categories();
        using var c = Connect();
        var (count, latest) = c.QueryFirst<(long, string?)>("SELECT COUNT(*), MAX(updated_at) FROM tracks");
        return string.Join("|", categories.Select(x => $"{x.Id}:{x.ParentId}:{x.Name}:{x.Kind}:{x.Color}:{x.SortOrder}")) + $"#{count}#{latest}";
    }

    public LibraryStats Stats()
    {
        using var c = Connect();
        var (tracks, missing, uncategorised, seconds) = c.QuerySingle<(long, long, long, double)>($"""
            SELECT COUNT(*),
                   COALESCE(SUM(missing), 0),
                   COALESCE(SUM(CASE WHEN category_id IS NULL AND missing = 0 AND {NotAlsoIn} THEN 1 ELSE 0 END), 0),
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
            "SELECT id AS Id, parent_id AS ParentId, name AS Name, kind AS Kind, color AS Color, sort_order AS SortOrder, role AS Role, "
            + "erc_portuguese AS ErcPortuguese, erc_language AS ErcLanguage, erc_nationality AS ErcNationality FROM categories ORDER BY sort_order, name")
            .ToList();
    }

    /// <summary>How many tracks (not missing) each category has: its own and those also in it (not counting the categories under it).</summary>
    public Dictionary<long, int> CategoryCounts()
    {
        using var c = Connect();
        return c.Query<(long Id, long Count)>("""
                SELECT category_id, COUNT(*) FROM (
                    SELECT id AS track_id, category_id FROM tracks WHERE category_id IS NOT NULL AND missing = 0
                    UNION
                    SELECT tc.track_id, tc.category_id FROM track_categories tc JOIN tracks t ON t.id = tc.track_id WHERE t.missing = 0
                ) AS m GROUP BY category_id
                """)
            .ToDictionary(r => r.Id, r => (int)r.Count);
    }

    /// <summary>
    /// Adds a category (under <paramref name="parentId"/>, or at the top). A name is unique among its siblings: the
    /// database sees to that under a parent, this at the top (where the database lets equal names through).
    /// </summary>
    public long AddCategory(string name, string kind, string? color, long? parentId = null)
    {
        // Everything goes under one of the eight fixed ones; a top-level name is taken as the fixed one of that name.
        if (parentId == null)
            return Categories().FirstOrDefault(x => FixedCategories.ByRole(x.Role) is { } f
                       && (Normalize(f.Name) == Normalize(name).Trim() || f.Aliases.Contains(Normalize(name).Trim())))?.Id
                ?? throw new InvalidOperationException($"New categories go under one of the station's eight ({string.Join(", ", FixedCategories.All.Select(f => f.Name))}).");
        using var c = Connect();
        if (parentId == null && c.ExecuteScalar<long>($"SELECT COUNT(*) FROM categories WHERE parent_id IS NULL AND {NoCase("name")} = @name", new { name }) > 0)
            throw new InvalidOperationException($"There is already a category called {name}.");
        int order = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), 0) + 1 FROM categories");
        return c.ExecuteScalar<long>($"""
            INSERT INTO categories (parent_id, name, kind, color, sort_order) VALUES (@parentId, @name, @kind, @color, @order);
            SELECT {(_mySql ? "LAST_INSERT_ID()" : "last_insert_rowid()")};
            """, new { parentId, name, kind, color, order });
    }

    /// <summary>Saves a category's name, kind, colour, order and place in the tree (never under itself or its own sub-categories).</summary>
    public void UpdateCategory(LibraryCategory category)
    {
        // A fixed one keeps its name, kind and place: only its colour changes.
        if (Categories().FirstOrDefault(x => x.Id == category.Id) is { IsFixed: true } fixedOne)
        {
            using var f = Connect();
            f.Execute("UPDATE categories SET color = @Color, erc_portuguese = @ErcPortuguese, erc_language = @ErcLanguage, erc_nationality = @ErcNationality WHERE id = @Id",
                new { category.Color, category.ErcPortuguese, category.ErcLanguage, category.ErcNationality, fixedOne.Id });
            return;
        }
        if (category.ParentId == null) throw new InvalidOperationException("A category goes under one of the station's eight.");
        using var c = Connect();
        if (category.ParentId is { } parent && CategoryTree.WithDescendants(Categories(), [category.Id]).Contains(parent))
            throw new InvalidOperationException("A category can't go under itself or one of its own sub-categories.");
        c.Execute("UPDATE categories SET parent_id = @ParentId, name = @Name, kind = @Kind, color = @Color, sort_order = @SortOrder, "
                  + "erc_portuguese = @ErcPortuguese, erc_language = @ErcLanguage, erc_nationality = @ErcNationality WHERE id = @Id", category);
    }

    /// <summary>Deletes a category: its songs lose it, its sub-categories move up to where it was.</summary>
    public void DeleteCategory(long id)
    {
        if (Categories().FirstOrDefault(x => x.Id == id) is { IsFixed: true } f) throw new InvalidOperationException($"{f.Name} is one of the station's eight categories: it can't be deleted.");
        using (var c = Connect())
        {
            using var tx = c.BeginTransaction();
            long? parent = c.ExecuteScalar<long?>("SELECT parent_id FROM categories WHERE id = @id", new { id }, tx);
            c.Execute("UPDATE categories SET parent_id = @parent WHERE parent_id = @id", new { id, parent }, tx);
            c.Execute("DELETE FROM categories WHERE id = @id", new { id }, tx);
            tx.Commit();
        }
        Rehome(); // its songs: the category of their folder, if another has one
    }

    // ───────────────────────── Editing ─────────────────────────

    /// <summary>
    /// Puts the songs of a folder (and its subfolders) in a category (null = none); with <paramref name="onlyWithout"/>,
    /// only those that have none yet (new songs found by a scan). Returns how many changed.
    /// </summary>
    public int SetFolderCategory(string folder, long? categoryId, bool onlyWithout = false) =>
        categoryId is { } id
            ? AssignFolder(folder, [], id, onlyWithout ? FolderAssign.OnlyWithout : FolderAssign.Replace)
            : ClearFolderCategory(folder);

    int ClearFolderCategory(string folder)
    {
        var args = new DynamicParameters();
        string under = Under(folder, "f", args);
        using var c = Connect();
        return c.Execute($"UPDATE tracks SET category_id = NULL WHERE {under}", args);
    }

    /// <summary>
    /// Takes a folder away from a category: its rule goes, and the songs under it leave that category, whether it is
    /// their own or one they are also in (other categories they have are kept). Those left with none get the category
    /// of a folder above it (Music for Music/80s), or one they are also in. Returns how many songs left the category.
    /// </summary>
    public int RemoveFolderCategory(string folder, long categoryId)
    {
        var args = new DynamicParameters();
        args.Add("category", categoryId);
        args.Add("now", Now());
        args.Add("folder", folder);
        string under = Under(folder, "f", args);
        int changed;
        using (var c = Connect())
        {
            using var tx = c.BeginTransaction();
            c.Execute("DELETE FROM category_folders WHERE path = @folder AND category_id = @category", args, tx);
            changed = c.Execute($"UPDATE tracks SET category_id = NULL, updated_at = @now WHERE {under} AND category_id = @category", args, tx);
            changed += c.Execute($"DELETE FROM track_categories WHERE category_id = @category AND track_id IN (SELECT id FROM tracks WHERE {under})", args, tx);
            tx.Commit();
        }
        Rehome();
        return changed;
    }

    /// <summary>
    /// Songs left without a category of their own: the folders' categories (the deepest folders first, as after a
    /// scan), then one they are also in.
    /// </summary>
    void Rehome()
    {
        foreach (var (f, id) in FolderCategories().OrderByDescending(r => r.Key.Length))
            SetFolderCategory(f, id, onlyWithout: true);
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute("""
            UPDATE tracks SET category_id = (SELECT MIN(x.category_id) FROM track_categories x WHERE x.track_id = tracks.id)
            WHERE category_id IS NULL AND EXISTS (SELECT 1 FROM track_categories x WHERE x.track_id = tracks.id)
            """, transaction: tx);
        c.Execute("""
            DELETE FROM track_categories
            WHERE EXISTS (SELECT 1 FROM tracks t WHERE t.id = track_categories.track_id AND t.category_id = track_categories.category_id)
            """, transaction: tx);
        tx.Commit();
    }

    /// <summary>
    /// Puts the songs under <paramref name="folder"/> in a category, leaving out those under <paramref name="except"/>
    /// (folders of its own sub-categories, given theirs). Songs that already have another category keep it and are
    /// also in this one (<see cref="FolderAssign.AlsoIn"/>), move to it, or are left alone. Returns how many changed.
    /// </summary>
    public int AssignFolder(string folder, IReadOnlyCollection<string> except, long categoryId, FolderAssign how)
    {
        var args = new DynamicParameters();
        args.Add("category", categoryId);
        args.Add("now", Now());
        string condition = Under(folder, "f", args);
        int n = 0;
        foreach (var other in except) condition += $" AND NOT {Under(other, "x" + n++, args)}";
        using var c = Connect();
        using var tx = c.BeginTransaction();
        // Without a category yet: this one, whichever way.
        int changed = c.Execute($"UPDATE tracks SET category_id = @category, updated_at = @now WHERE {condition} AND category_id IS NULL", args, tx);
        switch (how)
        {
            case FolderAssign.Replace:
                changed += c.Execute($"UPDATE tracks SET category_id = @category, updated_at = @now WHERE {condition} AND category_id <> @category", args, tx);
                c.Execute($"DELETE FROM track_categories WHERE category_id = @category AND track_id IN (SELECT id FROM tracks WHERE {condition})", args, tx);
                break;
            case FolderAssign.AlsoIn:
                changed += c.Execute($"""
                    INSERT {(_mySql ? "IGNORE" : "OR IGNORE")} INTO track_categories (track_id, category_id)
                    SELECT id, @category FROM tracks WHERE {condition} AND category_id <> @category
                    """, args, tx);
                break;
        }
        tx.Commit();
        return changed;
    }

    /// <summary>SQL: the path is under this folder (either kind of slash), with its parameters added.</summary>
    internal static string Under(string folder, string name, DynamicParameters args)
    {
        // As the scanner stores paths: full, without "." or ".." in them.
        if (!System.IO.Path.IsPathFullyQualified(folder) || folder.Contains("/.") || folder.Contains("\\.")) folder = System.IO.Path.GetFullPath(folder);
        string like = folder.TrimEnd('/', '\\').Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
        args.Add(name + "s", like + "/%");
        args.Add(name + "b", like + "\\%");
        return $"(path LIKE @{name}s ESCAPE '!' OR path LIKE @{name}b ESCAPE '!')";
    }

    /// <summary>The folders given to a category (folder → category id): their songs go in it, new ones after each scan too.</summary>
    public Dictionary<string, long> FolderCategories()
    {
        using var c = Connect();
        return c.Query<(string Path, long CategoryId)>("SELECT path AS Path, category_id AS CategoryId FROM category_folders")
            .ToDictionary(r => r.Path, r => r.CategoryId);
    }

    /// <summary>Gives a folder to a category, or (null) takes it away.</summary>
    public void SetFolderRule(string folder, long? categoryId)
    {
        using var c = Connect();
        c.Execute("DELETE FROM category_folders WHERE path = @folder", new { folder });
        if (categoryId is { } id) c.Execute("INSERT INTO category_folders (path, category_id) VALUES (@folder, @id)", new { folder, id });
    }

    /// <summary>Puts songs in a category too (keeping their own), or takes them out of it.</summary>
    public void SetAlsoIn(IEnumerable<long> trackIds, long categoryId, bool on)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        var rows = trackIds.Select(id => new { id, categoryId }).ToList();
        if (on)
            c.Execute($"INSERT {(_mySql ? "IGNORE" : "OR IGNORE")} INTO track_categories (track_id, category_id) VALUES (@id, @categoryId)", rows, tx);
        else
            c.Execute("DELETE FROM track_categories WHERE track_id = @id AND category_id = @categoryId", rows, tx);
        tx.Commit();
    }

    /// <summary>A song's own category (null: none). If it was also in that category, now it is just its own.</summary>
    public void SetCategory(IEnumerable<long> trackIds, long? categoryId)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        var rows = trackIds.Select(id => new { id, categoryId, now = Now() }).ToList();
        c.Execute("UPDATE tracks SET category_id = @categoryId, updated_at = @now WHERE id = @id", rows, tx);
        if (categoryId != null) c.Execute("DELETE FROM track_categories WHERE track_id = @id AND category_id = @categoryId", rows, tx);
        tx.Commit();
    }

    public void SetAttributes(IEnumerable<long> trackIds, TrackAttributes a)
    {
        var sets = new List<string> { "updated_at = @now" };
        if (a.ClearTempo) sets.Add("tempo = NULL"); else if (a.Tempo != null) sets.Add("tempo = @Tempo");
        if (a.ClearEnergy) sets.Add("energy = NULL"); else if (a.Energy != null) sets.Add("energy = @Energy");
        if (a.ClearVoice) sets.Add("voice = NULL"); else if (a.Voice != null) sets.Add("voice = @Voice");
        if (a.ClearLanguage) sets.Add("language = NULL"); else if (a.Language != null) sets.Add("language = @Language");
        if (a.Enabled != null) sets.Add("enabled = @Enabled");
        if (a.ClearErcPortuguese) sets.Add("erc_portuguese = NULL"); else if (a.ErcPortuguese != null) sets.Add("erc_portuguese = @ErcPortuguese");
        if (a.ClearErcNationality) sets.Add("erc_nationality = NULL"); else if (a.ErcNationality != null) sets.Add("erc_nationality = @ErcNationality");
        if (a.ClearErcRelease) sets.Add("erc_release = NULL"); else if (a.ErcRelease != null) sets.Add("erc_release = @ErcRelease");
        using var c = Connect();
        using var tx = c.BeginTransaction();
        c.Execute($"UPDATE tracks SET {string.Join(", ", sets)} WHERE id = @id",
            trackIds.Select(id => new { id, now = Now(), a.Tempo, a.Energy, a.Voice, a.Language, a.Enabled, a.ErcPortuguese, a.ErcNationality, a.ErcRelease }), tx);
        tx.Commit();
    }

    /// <summary>Re-reads one file's tags into its track (after its tags were edited); adds it if it isn't in the library.</summary>
    public void Refresh(string path) => LibraryScanner.ScanFile(this, path);

    // ───────────────────────── Cue points ─────────────────────────

    /// <summary>The time on the database server, in the form of <c>cues_updated_at</c>: what <see cref="CueChanges"/> takes.</summary>
    public string ServerTime()
    {
        using var c = Connect();
        return c.ExecuteScalar<string>($"SELECT {NowSql}")!;
    }

    /// <summary>The current UTC time on the database server, as text: every studio stamps cue edits with the same clock.</summary>
    string NowSql => _mySql ? "DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%dT%H:%i:%sZ')" : "strftime('%Y-%m-%dT%H:%M:%SZ', 'now')";

    /// <summary>
    /// Cue points set or cleared (empty) in the library since <paramref name="since"/> (a <see cref="ServerTime"/>),
    /// or all of them when null. Files whose cue points were never set in the library are left out.
    /// </summary>
    public List<(string Path, CuePoints Cues)> CueChanges(string? since)
    {
        using var c = Connect();
        string condition = since == null ? "IS NOT NULL" : ">= @since";
        return c.Query<(string Path, double? CueIn, double? Intro, double? Outro, double? Mix, double? CueOut)>(
                $"SELECT path, cue_in, intro, outro, mix, cue_out FROM tracks WHERE cues_updated_at {condition}", new { since })
            .Select(r => (r.Path, new CuePoints(r.CueIn, r.Intro, r.Outro, r.Mix, r.CueOut)))
            .ToList();
    }

    /// <summary>A file's cue points in the library (empty if none, or not in the library).</summary>
    public CuePoints GetCues(string path)
    {
        using var c = Connect();
        var r = c.QueryFirstOrDefault<(double? CueIn, double? Intro, double? Outro, double? Mix, double? CueOut)>(
            $"SELECT cue_in, intro, outro, mix, cue_out FROM tracks WHERE {PathIs("path")} ORDER BY id LIMIT 1", new { path });
        return new CuePoints(r.CueIn, r.Intro, r.Outro, r.Mix, r.CueOut);
    }

    /// <summary>Saves a file's cue points (empty clears them). False if the file isn't in the library.</summary>
    public bool SetCues(string path, CuePoints cues)
    {
        using var c = Connect();
        return c.Execute($"""
            UPDATE tracks SET cue_in = @CueIn, intro = @Intro, outro = @Outro, mix = @Mix, cue_out = @CueOut,
                cues_updated_at = {NowSql}
            WHERE {PathIs("path")}
            """, new { path, cues.CueIn, cues.Intro, cues.Outro, cues.Mix, cues.CueOut }) > 0;
    }

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

    /// <summary>When a file last went on air (UTC), in any studio sharing the library; null if never.</summary>
    public DateTime? LastStarted(string path)
    {
        using var c = Connect();
        var at = c.ExecuteScalar<string?>($"SELECT MAX(started_at) FROM plays WHERE {PathIs("path")}", new { path });
        return at == null ? null : ParseTime(at);
    }

    /// <summary>When each of these files last went on air (files never played are left out).</summary>
    public Dictionary<long, DateTime> LastPlayed(IEnumerable<long> trackIds)
    {
        using var c = Connect();
        return c.Query<(long Id, string At)>(
                "SELECT track_id, MAX(started_at) FROM plays WHERE track_id IN @ids GROUP BY track_id", new { ids = trackIds.ToArray() })
            .ToDictionary(r => r.Id, r => DateTime.Parse(r.At, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
    }

    /// <summary>
    /// Songs that may be picked automatically from these categories (enabled, file not missing), with when each
    /// last went on air (null = never).
    /// </summary>
    public List<RotationTrack> RotationTracks(IReadOnlyCollection<long> categoryIds)
    {
        if (categoryIds.Count == 0) return new();
        var categories = CategoryTree.WithDescendants(Categories(), categoryIds);
        using var c = Connect();
        return c.Query<RotationRow>("""
                SELECT t.id AS Id, t.path AS Path, t.artist AS Artist, t.title AS Title, t.duration AS Duration,
                       t.cue_in AS CueIn, t.cue_out AS CueOut, t.mix AS Mix, t.album AS Album, t.tempo AS Tempo, t.energy AS Energy,
                       t.play_from AS PlayFrom, t.play_until AS PlayUntil, t.play_days AS PlayDays, t.play_hours AS PlayHours,
                       t.max_day AS MaxDay, t.max_week AS MaxWeek,
                       (SELECT MAX(p.started_at) FROM plays p WHERE p.track_id = t.id) AS Last
                FROM tracks t
                WHERE t.enabled = 1 AND t.missing = 0
                  AND (t.category_id IN @categories OR t.id IN (SELECT track_id FROM track_categories WHERE category_id IN @categories))
                """, new { categories })
            .Select(r => new RotationTrack(r.Id, r.Path, r.Artist, r.Last == null ? null : ParseTime(r.Last), r.Title, r.Duration, r.CueIn, r.CueOut, r.Mix,
                r.Album, r.Tempo, r.Energy, SongRestrictions.Make(r.PlayFrom, r.PlayUntil, r.PlayDays, r.PlayHours, r.MaxDay, r.MaxWeek)))
            .ToList();
    }

    sealed class RotationRow
    {
        public long Id { get; set; }
        public string Path { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public double Duration { get; set; }
        public double? CueIn { get; set; }
        public double? CueOut { get; set; }
        public double? Mix { get; set; }
        public string Album { get; set; } = "";
        public int? Tempo { get; set; }
        public int? Energy { get; set; }
        public string? Last { get; set; }
        public string? PlayFrom { get; set; }
        public string? PlayUntil { get; set; }
        public string? PlayDays { get; set; }
        public string? PlayHours { get; set; }
        public int? MaxDay { get; set; }
        public int? MaxWeek { get; set; }
    }

    /// <summary>What went on air since <paramref name="sinceUtc"/>, in any studio sharing the library.</summary>
    /// <summary>The files that went on air between two times (UTC), in order, with when (UTC).</summary>
    public List<(string Path, DateTime At)> PlayedFiles(DateTime fromUtc, DateTime toUtc)
    {
        using var c = Connect();
        return c.Query<(string Path, string At)>(
                "SELECT path, started_at FROM plays WHERE started_at >= @from AND started_at < @to ORDER BY started_at",
                new { from = Time(fromUtc), to = Time(toUtc) })
            .Select(r => (r.Path, ParseTime(r.At)))
            .ToList();
    }

    public List<RecentPlay> PlaysSince(DateTime sinceUtc)
    {
        using var c = Connect();
        return c.Query<(long? TrackId, string Artist, string At, string? Album, int? Tempo)>(
                "SELECT p.track_id, p.artist, p.started_at, t.album, t.tempo FROM plays p LEFT JOIN tracks t ON t.id = p.track_id WHERE p.started_at >= @since",
                new { since = Time(sinceUtc) })
            .Select(r => new RecentPlay(r.TrackId, r.Artist, ParseTime(r.At), r.Album ?? "", r.Tempo))
            .ToList();
    }

    /// <summary>The last item that went on air in any studio (UTC), or null.</summary>
    public (string Title, string Artist, DateTime At)? LastPlay()
    {
        using var c = Connect();
        var r = c.QueryFirstOrDefault<(string Title, string Artist, string At)?>(
            $"SELECT title, artist, started_at FROM plays WHERE source = 'playlist' ORDER BY started_at DESC {(_mySql ? "LIMIT 1" : "LIMIT 1")}");
        return r is { } p ? (p.Title, p.Artist, ParseTime(p.At)) : null;
    }

    /// <summary>The album and tempo of these tracks (for the music rules of what is planned).</summary>
    public Dictionary<long, (string Album, int? Tempo)> AlbumsAndTempos(IReadOnlyCollection<long> trackIds)
    {
        var result = new Dictionary<long, (string, int?)>();
        if (trackIds.Count == 0) return result;
        using var c = Connect();
        foreach (var chunk in trackIds.Distinct().Chunk(500))
            foreach (var r in c.Query<(long Id, string Album, int? Tempo)>("SELECT id, album, tempo FROM tracks WHERE id IN @ids", new { ids = chunk }))
                result[r.Id] = (r.Album, r.Tempo);
        return result;
    }

    // ───────────────────────── Helpers ─────────────────────────

    static DateTime ParseTime(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static string Now() => Time(DateTime.UtcNow);

    public static string Time(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

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
