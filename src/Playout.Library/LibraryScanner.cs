using System.Data.Common;
using Dapper;
using Playout.Engine;

namespace Playout.Library;

public sealed record ScanProgress(int Found, int ToRead, int Read);

/// <param name="Added">New files.</param>
/// <param name="Updated">Changed files read again.</param>
/// <param name="Moved">Files found under a new path or name (same size and tags): category and history kept.</param>
/// <param name="Removed">Files no longer found (deleted, or in a folder that was deleted): taken out of the library.</param>
/// <param name="Unchanged">Files not read again (same size and date).</param>
/// <param name="NoTags">Files whose tags couldn't be read (added with the file name as title).</param>
/// <param name="TooLong">Files skipped because their path is longer than <see cref="LibraryDb.MaxPathLength"/> characters.</param>
/// <param name="SkippedFolders">Folders that weren't there or had no audio (e.g. a network share that is offline): nothing in them was marked missing.</param>
public sealed record ScanResult(int Added, int Updated, int Moved, int Removed, int Unchanged, int NoTags, int TooLong,
    IReadOnlyList<string> SkippedFolders, TimeSpan Elapsed, bool Cancelled);

/// <summary>
/// Brings the library up to date with music folders. Only new or changed files (size or date) are opened, and only
/// their headers and tags are read, so a rescan of a large library is quick. Used by the app and by radio-import.
/// </summary>
public static class LibraryScanner
{
    public static readonly string[] Extensions = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".mp2", ".aiff", ".aif"];

    const int Batch = 256;     // files read, then written in one transaction
    const int Readers = 4;     // files read at once (helps with network shares)

    internal static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    internal static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static bool IsAudio(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    sealed record Existing(long Id, long FileSize, string? FileModified, bool Missing);

    /// <param name="Path">As the library keeps it (see <see cref="SharedPaths"/>).</param>
    /// <param name="Local">Where it is on this computer, when that is another path.</param>
    sealed record FileInfoRow(string Path, long Size, string Modified, string? Local = null)
    {
        public string Disk => Local ?? Path;
    }

    sealed record Read(FileInfoRow File, string Title, string Artist, string Album, string Genre, int? Year, string? Isrc,
        double Duration, bool HasTags);

    public static ScanResult Scan(LibraryDb db, IEnumerable<string> folders, IProgress<ScanProgress>? progress = null,
        CancellationToken cancel = default)
    {
        var started = DateTime.UtcNow;
        using var c = db.Connect();
        // (TryAdd: rows another program added that differ only in case are the same file here; the first one wins.)
        var existing = new Dictionary<string, Existing>(PathComparer);
        foreach (var r in c.Query<(long Id, string Path, long Size, string? Modified, long Missing)>(
                     "SELECT id, path, file_size, file_modified, missing FROM tracks ORDER BY id"))
            existing.TryAdd(r.Path, new Existing(r.Id, r.Size, r.Modified, r.Missing != 0));

        // 1. Walk the folders.
        var roots = new List<string>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        var toRead = new List<FileInfoRow>();
        int unchanged = 0, tooLong = 0;
        foreach (var folder in folders.Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(SharedPaths.ToLocal(f)))).Distinct(PathComparer))
        {
            int before = seen.Count;
            if (Directory.Exists(folder))
            {
                foreach (var file in Files(folder))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (!seen.Add(file.Path)) continue;
                    if (file.Path.Length > LibraryDb.MaxPathLength) { tooLong++; continue; }
                    if (existing.TryGetValue(file.Path, out var e) && !e.Missing && e.FileSize == file.Size && e.FileModified == file.Modified)
                        unchanged++;
                    else
                        toRead.Add(file);
                    if (seen.Count % 500 == 0) progress?.Report(new ScanProgress(seen.Count, toRead.Count, 0));
                }
            }
            // (Roots as the library keeps its paths: the songs under them are compared with its.)
            if (seen.Count > before) roots.Add(SharedPaths.ToLibrary(folder));
            // Gone, while the folder it was in is there: deleted (or moved away), so are its songs. A folder whose
            // parent is gone too, or that is there but empty, is more likely a drive or share that isn't connected:
            // left alone.
            else if (!Directory.Exists(folder) && Path.GetDirectoryName(folder) is { } parent && Directory.Exists(parent)) roots.Add(SharedPaths.ToLibrary(folder));
            else skipped.Add(folder);
        }
        progress?.Report(new ScanProgress(seen.Count, toRead.Count, 0));

        // Tracks under the scanned folders that weren't found: missing, unless they turn up under a new name.
        var vanished = existing
            .Where(kv => !seen.Contains(kv.Key) && roots.Any(r => IsUnder(kv.Key, r)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, PathComparer);
        var vanishedTags = vanished.Count == 0
            ? new()
            : c.Query<(long Id, string Path, long Size, string Artist, string Title)>(
                    "SELECT id, path, file_size, artist, title FROM tracks WHERE missing = 0")
                .Where(r => vanished.ContainsKey(r.Path))
                .ToList();

        var categories = db.Categories();
        long? jingles = categories.FirstOrDefault(x => x.Kind == "jingle")?.Id;
        long? ads = categories.FirstOrDefault(x => x.Kind == "commercial")?.Id;

        // 2. Read new and changed files, a batch at a time, and write each batch in one transaction.
        int added = 0, updated = 0, moved = 0, noTags = 0, read = 0;
        bool cancelled = false;
        try
        {
            foreach (var chunk in toRead.Chunk(Batch))
            {
                cancel.ThrowIfCancellationRequested();
                var results = new Read[chunk.Length];
                Parallel.For(0, chunk.Length, new ParallelOptions { MaxDegreeOfParallelism = Readers, CancellationToken = cancel },
                    i => results[i] = ReadFile(chunk[i]));

                using var tx = c.BeginTransaction();
                foreach (var r in results)
                {
                    if (!r.HasTags) noTags++;
                    if (existing.TryGetValue(r.File.Path, out var e))
                    {
                        Update(c, tx, e.Id, r);
                        updated++;
                    }
                    else if (TakeMoved(r) is { } oldId)
                    {
                        Update(c, tx, oldId, r);
                        moved++;
                    }
                    else
                    {
                        Insert(db, c, tx, r, TrackClassifier.Classify(r.File.Path) switch
                        {
                            TrackKind.Jingle => jingles,
                            TrackKind.Commercial => ads,
                            _ => null,
                        });
                        added++;
                    }
                }
                tx.Commit();
                read += chunk.Length;
                progress?.Report(new ScanProgress(seen.Count, toRead.Count, read));
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        // 3. What is still unaccounted for was deleted: out of the library (the play history keeps its artist, title
        // and time). If something still points at it (a table of an older version), it is marked missing instead.
        int removed = 0;
        if (!cancelled && vanished.Count > 0)
        {
            var gone = vanished.Values.Select(v => new { v.Id, now = LibraryDb.Now() }).ToList();
            try
            {
                using var tx = c.BeginTransaction();
                removed = c.Execute("DELETE FROM tracks WHERE id = @Id", gone, tx);
                tx.Commit();
            }
            catch (DbException)
            {
                using var tx = c.BeginTransaction();
                removed = c.Execute("UPDATE tracks SET missing = 1, updated_at = @now WHERE id = @Id", gone, tx);
                tx.Commit();
            }
        }

        return new ScanResult(added, updated, moved, removed, unchanged, noTags, tooLong, skipped, DateTime.UtcNow - started, cancelled);

        // A new file that is a vanished one moved or renamed: same size and file name, or same size, artist and title.
        long? TakeMoved(Read r)
        {
            string name = Path.GetFileName(r.File.Path);
            // The same file name first: with duplicates (same size and tags) that is the one that moved.
            var match = vanishedTags.FirstOrDefault(v => v.Size == r.File.Size && Path.GetFileName(v.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Path == null && r.HasTags)
                match = vanishedTags.FirstOrDefault(v => v.Size == r.File.Size && v.Artist == r.Artist && v.Title == r.Title);
            if (match.Path == null) return null;
            vanishedTags.Remove(match);
            vanished.Remove(match.Path);
            return match.Id;
        }
    }

    /// <summary>Adds or re-reads one file (e.g. after its tags were edited in the app).</summary>
    public static void ScanFile(LibraryDb db, string path)
    {
        path = SharedPaths.ToLocal(path);
        if (!File.Exists(path) || !IsAudio(path)) return;
        var info = new FileInfo(path);
        var r = ReadFile(Row(info));
        if (r.File.Path.Length > LibraryDb.MaxPathLength) return;
        using var c = db.Connect();
        using var tx = c.BeginTransaction();
        if (db.TrackIdByPath(c, r.File.Path, tx) is { } id)
            Update(c, tx, id, r);
        else Insert(db, c, tx, r, null);
        tx.Commit();
    }

    static IEnumerable<FileInfoRow> Files(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*", options))
            if (IsAudio(info.Name)) yield return Row(info);
    }

    /// <summary>A file found on this computer, with its path as the library keeps it.</summary>
    static FileInfoRow Row(FileInfo info)
    {
        string library = SharedPaths.ToLibrary(info.FullName);
        return new FileInfoRow(library, info.Length, Modified(info), library == info.FullName ? null : info.FullName);
    }

    static string Modified(FileInfo info) => LibraryDb.Time(info.LastWriteTimeUtc);

    internal static bool IsUnder(string path, string root) =>
        path.Length > root.Length && path.StartsWith(root, PathComparison) &&
        (IsSeparator(root[^1]) || IsSeparator(path[root.Length])); // a drive root (D:\ or /) keeps its separator

    static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    /// <summary>Tags and length from the file's headers (never decodes the audio, so big files are as quick as small ones).</summary>
    static Read ReadFile(FileInfoRow file)
    {
        string title = "", artist = "", album = "", genre = "";
        int? year = null;
        string? isrc = null;
        double duration = 0;
        bool hasTags = false;
        try
        {
            using var f = TagLib.File.Create(file.Disk, TagLib.ReadStyle.Average);
            var t = f.Tag;
            title = t.Title?.Trim() ?? "";
            artist = (t.FirstPerformer ?? t.FirstAlbumArtist)?.Trim() ?? "";
            album = t.Album?.Trim() ?? "";
            genre = t.FirstGenre?.Trim() ?? "";
            year = t.Year > 0 ? (int)t.Year : null;
            isrc = string.IsNullOrWhiteSpace(t.ISRC) ? null : t.ISRC.Trim();
            duration = f.Properties?.Duration.TotalSeconds ?? 0; // only if BASS can't tell (below)
            hasTags = true;
        }
        catch
        {
            // Unknown format or damaged tags: the file name has to do.
        }
        // BASS reads the length as the player will; TagLib gets some MP3s wrong (e.g. a different bitrate in the first frame).
        duration = AudioProbe.Duration(file.Disk) ?? duration;
        if (title.Length == 0)
        {
            // "Artist - Title.mp3"
            var parts = Path.GetFileNameWithoutExtension(file.Path).Split(" - ", 2);
            if (parts.Length == 2) (artist, title) = (artist.Length > 0 ? artist : parts[0].Trim(), parts[1].Trim());
            else title = parts[0].Trim();
        }
        return new Read(file, title, artist, album, genre, year, isrc, duration, hasTags);
    }

    /// <summary>Adds a file. If another studio's scan added it a moment ago (a shared library), theirs is kept.</summary>
    static void Insert(LibraryDb db, DbConnection c, DbTransaction tx, Read r, long? categoryId)
    {
        string now = LibraryDb.Now();
        c.Execute($"""
            {(db.IsMySql ? "INSERT IGNORE" : "INSERT OR IGNORE")} INTO tracks (path, file_size, file_modified, title, artist, album, genre, year, isrc, duration,
                                category_id, added_at, updated_at, search)
            VALUES (@Path, @Size, @Modified, @Title, @Artist, @Album, @Genre, @Year, @Isrc, @Duration,
                    @categoryId, @now, @now, @search)
            """, Args(r, now, new { categoryId }), tx);
    }

    /// <summary>New file details and tags; category, attributes and history stay.</summary>
    static void Update(DbConnection c, DbTransaction tx, long id, Read r)
    {
        c.Execute("""
            UPDATE tracks SET path = @Path, file_size = @Size, file_modified = @Modified, title = @Title, artist = @Artist,
                album = @Album, genre = @Genre, year = @Year, isrc = COALESCE(@Isrc, isrc), duration = @Duration,
                missing = 0, updated_at = @now, search = @search
            WHERE id = @id
            """, Args(r, LibraryDb.Now(), new { id }), tx);
    }

    static DynamicParameters Args(Read r, string now, object extra)
    {
        var p = new DynamicParameters(extra);
        p.Add("Path", r.File.Path);
        p.Add("Size", r.File.Size);
        p.Add("Modified", r.File.Modified);
        // Cut to the column sizes (MySQL refuses longer text).
        p.Add("Title", Cut(r.Title, 500));
        p.Add("Artist", Cut(r.Artist, 500));
        p.Add("Album", Cut(r.Album, 500));
        p.Add("Genre", Cut(r.Genre, 200));
        p.Add("Year", r.Year);
        p.Add("Isrc", r.Isrc is { } isrc ? Cut(isrc, 20) : null);
        p.Add("Duration", Math.Round(r.Duration, 3));
        p.Add("now", now);
        p.Add("search", Cut(LibraryDb.SearchText(r.File.Path, r.Artist, r.Title, r.Album, r.Genre), 2000));
        return p;
    }

    static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
