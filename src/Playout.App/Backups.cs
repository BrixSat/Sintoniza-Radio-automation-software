using System.IO.Compression;
using Playout.Library;

namespace Playout.App;

/// <summary>Daily backups: when, how many days are kept, and where (empty = the backups folder next to the settings).</summary>
public sealed class BackupSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>"HH:mm", local time.</summary>
    public string Time { get; set; } = "04:00";
    public int KeepDays { get; set; } = 14;
    public string Folder { get; set; } = "";

    public string FolderOrDefault => string.IsNullOrWhiteSpace(Folder) ? Backups.DefaultFolder : Folder;
}

/// <summary>
/// A zip a day of everything the station needs to be put back: the database (SQLite copied, MySQL dumped) and the
/// files next to the settings (settings, the playout's copy of the schedule, cue points, as-run logs). Made off the UI thread; old ones are
/// removed after the days to keep, but the newest three always stay.
/// </summary>
sealed class Backups : IDisposable
{
    public static string DefaultFolder => Path.Combine(AppSettings.DataFolder, "backups");
    const string Prefix = "sintoniza-";
    static readonly string[] DataFiles = ["settings.json", "schedule-cache.json", "cues.json", "loudness.txt"]; // the schedule itself is in the database

    readonly Func<BackupSettings> _settings;
    readonly Func<LibraryDb?> _db;
    readonly Timer _timer;
    int _running;

    /// <summary>A line for the status bar and the log (raised off the UI thread).</summary>
    public event Action<string>? Message;
    /// <summary>A backup failed: for the alerts.</summary>
    public event Action<string>? Failed;

    public Backups(Func<BackupSettings> settings, Func<LibraryDb?> db)
    {
        _settings = settings;
        _db = db;
        _timer = new Timer(_ => { if (IsDue(DateTime.Now)) _ = RunAsync(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>The newest backup in the folder (null if none).</summary>
    public FileInfo? Last
    {
        get
        {
            try
            {
                var dir = new DirectoryInfo(_settings().FolderOrDefault);
                return dir.Exists ? dir.GetFiles(Prefix + "*.zip").MaxBy(f => f.LastWriteTimeUtc) : null;
            }
            catch { return null; }
        }
    }

    /// <summary>Today's backup time has passed and there is none since then.</summary>
    bool IsDue(DateTime now)
    {
        var s = _settings();
        if (!s.Enabled || !TimeSpan.TryParse(s.Time, out var at)) return false;
        var due = now.Date + at;
        return now >= due && (Last?.LastWriteTime ?? DateTime.MinValue) < due;
    }

    /// <summary>Makes a backup now (one at a time); returns its file, or null if it failed or one is being made.</summary>
    public Task<string?> RunAsync() => Task.Run(() =>
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return null;
        try { return Run(); }
        catch (Exception ex)
        {
            Say(L.F("Backup failed: {0}", ex.Message));
            Failed?.Invoke(ex.Message);
            return null;
        }
        finally { Volatile.Write(ref _running, 0); }
    });

    string Run()
    {
        var s = _settings();
        string folder = s.FolderOrDefault;
        Directory.CreateDirectory(folder);
        string name = $"{Prefix}{DateTime.Now:yyyy-MM-dd-HHmm}";
        string work = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(work);
        try
        {
            string? database = _db()?.Backup(Path.Combine(work, "database"));
            string zip = Path.Combine(folder, name + ".zip"), part = zip + ".part";
            using (var archive = ZipFile.Open(part, ZipArchiveMode.Create))
            {
                if (database != null) archive.CreateEntryFromFile(database, Path.GetFileName(database), CompressionLevel.Optimal);
                foreach (var file in DataFiles) Add(archive, Path.Combine(AppSettings.DataFolder, file), file);
                string asRun = Path.Combine(AppSettings.DataFolder, "as-run");
                if (Directory.Exists(asRun))
                    foreach (var file in Directory.GetFiles(asRun, "*.csv")) Add(archive, file, "as-run/" + Path.GetFileName(file));
            }
            File.Move(part, zip, true);
            int removed = Prune(folder, s.KeepDays);
            Say(L.F("Backup made: {0} ({1:0.0} MB){2}", Path.GetFileName(zip), new FileInfo(zip).Length / 1048576.0,
                database == null ? L.T(", without the database (not open)") : "") + (removed > 0 ? L.F("; {0} old ones removed", removed) : ""));
            return zip;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>A file that may be being written (as-run, settings): read shared.</summary>
    static void Add(ZipArchive archive, string path, string entry)
    {
        if (!File.Exists(path)) return;
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = archive.CreateEntry(entry, CompressionLevel.Optimal).Open();
        source.CopyTo(target);
    }

    /// <summary>Removes backups older than <paramref name="keepDays"/>, keeping the newest three.</summary>
    static int Prune(string folder, int keepDays)
    {
        var old = new DirectoryInfo(folder).GetFiles(Prefix + "*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(3)
            .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-Math.Max(1, keepDays))).ToList();
        foreach (var f in old)
            try { f.Delete(); } catch { }
        return old.Count;
    }

    void Say(string message) => Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");

    public void Dispose() => _timer.Dispose();
}
