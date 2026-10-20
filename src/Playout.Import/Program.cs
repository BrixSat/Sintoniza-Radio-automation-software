using Playout.Library;

// radio-import: brings the music library up to date with folders on disk. Can run while the app is on air,
// and on a schedule (cron, Task Scheduler) to pick up new music.

const string Usage = """
    Usage: radio-import [library] [FOLDER...]
           radio-import [library] --stats

    Adds new music files in the folders (and their subfolders) to the library, reads changed ones again, follows
    files that were moved or renamed, and takes files that are gone (or in a deleted folder) out of the library. Only new and changed files are
    opened, and only their tags are read, so running it again is quick. Without folders, it scans the folders
    set in the app (Settings → Library).

    The library is the app's (Settings → Library) unless given:
      --db FILE                     an SQLite library file
      --mysql USER:PASSWORD@HOST[:PORT]/DATABASE
                                    a MySQL / MariaDB library
      --stats     show what is in the library
      --quiet     only print the summary
    """;

var library = LibrarySettings.FromApp();
bool stats = false, quiet = false;
var folders = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db" when i + 1 < args.Length:
            library = new LibrarySettings { File = args[++i], Folders = library.Folders };
            break;
        case "--mysql" when i + 1 < args.Length:
            if (ParseMySql(args[++i]) is not { } my)
            {
                Console.Error.WriteLine("--mysql needs USER:PASSWORD@HOST[:PORT]/DATABASE");
                return 2;
            }
            my.Folders = library.Folders;
            library = my;
            break;
        case "--stats": stats = true; break;
        case "--quiet" or "-q": quiet = true; break;
        case "--help" or "-h" or "/?":
            Console.WriteLine(Usage);
            return 0;
        default:
            if (args[i].StartsWith("--"))
            {
                Console.Error.WriteLine($"Unknown option {args[i]}\n");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            folders.Add(args[i]);
            break;
    }
}
if (folders.Count == 0 && !stats)
{
    folders = library.Folders.ToList();
    if (folders.Count == 0)
    {
        Console.Error.WriteLine("No folders given, and none set in the app (Settings → Library).\n");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

LibraryDb db;
try
{
    db = LibraryDb.Open(library);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot open the library {library.Describe()}: {ex.Message}");
    return 1;
}

if (folders.Count > 0)
{
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // finish the batch being written, then stop
        cancel.Cancel();
        Console.Error.WriteLine("\nStopping…");
    };

    if (!quiet) Console.WriteLine($"Library: {db.Location}");
    var lastReport = DateTime.MinValue;
    var progress = new SyncProgress<ScanProgress>(p =>
    {
        if (quiet || (DateTime.UtcNow - lastReport).TotalSeconds < 1) return;
        lastReport = DateTime.UtcNow;
        Console.Write(p.ToRead == 0 || p.Read == 0
            ? $"\rLooking for files: {p.Found:N0} found, {p.ToRead:N0} new or changed      "
            : $"\rReading tags: {p.Read:N0} of {p.ToRead:N0} ({100.0 * p.Read / p.ToRead:0}%)      ");
    });

    ScanResult r;
    try
    {
        r = LibraryScanner.Scan(db, folders, progress, cancel.Token);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Stopped before any file was read.");
        return 130;
    }
    if (!quiet) Console.Write("\r" + new string(' ', 70) + "\r");
    Console.WriteLine($"Added {r.Added:N0}, updated {r.Updated:N0}, moved {r.Moved:N0}, removed {r.Removed:N0}, " +
                      $"unchanged {r.Unchanged:N0} in {r.Elapsed.TotalSeconds:0.0} s{(r.Cancelled ? " (stopped early)" : "")}");
    if (r.TooLong > 0) Console.WriteLine($"{r.TooLong:N0} files skipped: path longer than {LibraryDb.MaxPathLength} characters.");
    if (r.NoTags > 0) Console.WriteLine($"{r.NoTags:N0} files had no readable tags; their file name is used as the title.");
    foreach (var f in r.SkippedFolders) Console.WriteLine($"Skipped {f}: not found (nor the folder it is in) or no audio files: nothing in it was removed.");
    if (r.Cancelled) return 130;
}

if (stats || !quiet)
{
    var s = db.Stats();
    Console.WriteLine($"Library: {s.Tracks:N0} tracks, {s.Hours:N0} hours; {s.Uncategorised:N0} without a category, {s.Missing:N0} missing.");
}
return 0;

/// <summary>USER:PASSWORD@HOST[:PORT]/DATABASE (the password may contain ':' but not '@').</summary>
static LibrarySettings? ParseMySql(string text)
{
    int at = text.LastIndexOf('@'), slash = text.IndexOf('/', Math.Max(at, 0));
    if (at <= 0 || slash < 0) return null;
    var (user, password) = text[..at].Split(':', 2) is [var u, var p] ? (u, p) : (text[..at], "");
    var host = text[(at + 1)..slash].Split(':', 2);
    int port = 3306;
    if (host.Length == 2 && !int.TryParse(host[1], out port)) return null;
    return new LibrarySettings
    {
        Provider = "mysql", User = user, Password = password, Server = host[0], Port = port, Database = text[(slash + 1)..],
    };
}

/// <summary>Reports on the scanning thread (Progress&lt;T&gt; would post to the thread pool and reorder lines).</summary>
sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
