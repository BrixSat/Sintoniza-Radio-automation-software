using System.Globalization;
using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The app's connection to the music library: opens it (in the background: a MySQL server can take a while to
/// answer, and is tried again every minute while it can't be reached), runs scans (on request, on a schedule and
/// when files in the music folders change), keeps the cue points in step with it, and writes the play history in
/// order, off the UI and engine threads.
/// </summary>
sealed class LibraryService
{
    static readonly TimeSpan RetryOpen = TimeSpan.FromMinutes(1);
    static readonly TimeSpan CueSyncInterval = TimeSpan.FromSeconds(30);
    /// <summary>Files are still being copied while changes keep coming: scan once the folders are quiet this long.</summary>
    static readonly TimeSpan WatchQuiet = TimeSpan.FromSeconds(20);
    /// <summary>The first scheduled scan, after the app has started (not while everything is still loading).</summary>
    static readonly TimeSpan FirstScan = TimeSpan.FromMinutes(1);

    static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    Task _writes = Task.CompletedTask;
    int _openVersion;
    CancellationTokenSource? _scan;
    LibrarySettings? _settings;
    DateTime _nextOpen = DateTime.MaxValue;
    readonly DispatcherTimer _timer;

    /// <summary>The open library, or null (not set up yet, opening, or it failed: see <see cref="Error"/>).</summary>
    public LibraryDb? Db { get; private set; }
    public string? Error { get; private set; }
    public bool IsScanning => _scan != null;

    /// <summary>Raised on the UI thread when the library was (re)opened, or its contents changed (scan, edits).</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread with a line for the status bar and the log.</summary>
    public event Action<string>? Message;

    public LibraryService()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    public async Task OpenAsync(LibrarySettings settings)
    {
        int version = ++_openVersion;
        _settings = settings.Clone();
        _nextOpen = DateTime.MaxValue;
        Db = null;
        Error = null;
        _cueSince = null;
        Changed?.Invoke();
        var copy = settings.Clone();
        try
        {
            var db = await Task.Run(() => LibraryDb.Open(copy));
            if (version != _openVersion) return; // settings changed again meanwhile
            Db = db;
            SyncCues();
            _stamp = null;
            _readingStamp = false;
            ReadStamp(db, notify: false); // how it is now, to notice changes made elsewhere
        }
        catch (Exception ex)
        {
            if (version != _openVersion) return;
            bool first = Error == null && _lastOpenError != ex.Message;
            Error = L.F("Library unavailable ({0}): {1}", copy.Describe(), DbErrors.Explain(ex, copy));
            _lastOpenError = ex.Message;
            _nextOpen = DateTime.UtcNow + RetryOpen;
            if (first) Message?.Invoke(L.F("{0}. Trying again every minute.", Error.TrimEnd('.')));
            else AppLog.Write(Error);
        }
        Changed?.Invoke();
    }

    string? _lastOpenError;

    /// <summary>Tells the views the contents changed (after an edit).</summary>
    /// <summary>This program changed the library: everything showing it reads it again (and the next check knows it was this).</summary>
    public void NotifyChanged()
    {
        Changed?.Invoke();
        if (Db is { } db) { _readingStamp = false; ReadStamp(db, notify: false); }
    }

    /// <summary>Runs <paramref name="write"/> after the writes queued before it, off the UI thread.</summary>
    void Queue(Action write)
    {
        lock (this)
            _writes = _writes.ContinueWith(_ => write(), TaskScheduler.Default);
    }

    /// <summary>Adds an item that went on air to the history. Safe from any thread; written in order.</summary>
    public void RecordPlay(Track track, string source) => RecordPlay(track.Path, track.Artist, track.Title, source);

    public void RecordPlay(string path, string artist, string title, string source)
    {
        if (Db is not { } db) return;
        var at = DateTime.UtcNow;
        Queue(() =>
        {
            try { db.RecordPlay(path, artist, title, at, source); }
            catch (Exception ex) { Post(L.F("Play history not saved: {0}", ex.Message)); }
        });
    }

    /// <summary>Re-reads a file's tags into the library (after editing them).</summary>
    public void Refresh(string path)
    {
        if (Db is not { } db) return;
        Task.Run(() =>
        {
            try { db.Refresh(path); }
            catch (Exception ex) { Post(L.F("Library not updated: {0}", ex.Message)); return; }
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        });
    }

    void Tick()
    {
        if (Db == null && _settings != null && DateTime.UtcNow >= _nextOpen)
        {
            _nextOpen = DateTime.MaxValue; // set again if it fails
            _ = OpenAsync(_settings);
            return;
        }
        if (Db == null) return;
        if (DateTime.UtcNow - _cueSynced >= CueSyncInterval) SyncCues();
        if (DateTime.UtcNow - _stampRead >= StampInterval) ReadStamp(Db);
        if (DateTime.UtcNow - _categoryFoldersRead >= CategoryFoldersInterval) ReadCategoryFolders(Db);
        AutoScan();
    }

    // ───────────────────────── Scanning ─────────────────────────

    IReadOnlyList<string> _autoFolders = [];

    // The folders of categories (Sintoniza Import, Categories…) are in the database, shared by every computer: they
    // are scanned and watched too, with the music folders of Settings → Library (those here that exist).
    static readonly TimeSpan CategoryFoldersInterval = TimeSpan.FromMinutes(5);
    IReadOnlyList<string> _categoryFolders = [];
    DateTime _categoryFoldersRead;
    bool _readingCategoryFolders;

    /// <summary>The category folders on this computer, as last read from the database.</summary>
    public IReadOnlyList<string> CategoryFolders => _categoryFolders;

    void ReadCategoryFolders(LibraryDb db)
    {
        if (_readingCategoryFolders) return;
        _readingCategoryFolders = true;
        _categoryFoldersRead = DateTime.UtcNow;
        Task.Run(() => db.FolderCategories().Keys.Select(SharedPaths.ToLocal).Where(Directory.Exists).Order(PathComparer).ToList())
            .ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                _readingCategoryFolders = false;
                if (!t.IsCompletedSuccessfully || t.Result.SequenceEqual(_categoryFolders, PathComparer)) return;
                _categoryFolders = t.Result;
                if (_watch) Watch();
            }));
    }

    /// <summary>
    /// These folders and the category folders, leaving out any inside another (scanned with it already).
    /// </summary>
    IReadOnlyList<string> WithCategoryFolders(IReadOnlyList<string> folders)
    {
        var all = folders.Concat(_categoryFolders).Select(f => f.TrimEnd('/', '\\')).Where(f => f.Length > 0)
            .Distinct(PathComparer).ToList();
        return all.Where(f => !all.Any(o => !PathComparer.Equals(o, f) && Inside(f, o))).ToList();
    }

    static bool Inside(string path, string folder) =>
        path.Length > folder.Length && path.StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
        && path[folder.Length] is '/' or '\\';
    int _autoMinutes;
    bool _watch;
    readonly List<FileSystemWatcher> _watchers = new();
    readonly HashSet<string> _watched = new(PathComparer);
    DateTime? _lastScan; // UTC; null = none since the app started
    readonly DateTime _started = DateTime.UtcNow;
    DateTime _lastChange; // UTC; set from watcher threads
    volatile bool _dirty;

    /// <summary>
    /// Scans the music folders every <paramref name="minutes"/> minutes (0 = not on a schedule) and, with
    /// <paramref name="watch"/>, shortly after files in them are added, changed, moved or deleted.
    /// </summary>
    public void ConfigureAutoScan(IReadOnlyList<string> folders, int minutes, bool watch)
    {
        _autoFolders = folders.ToList();
        _autoMinutes = Math.Max(0, minutes);
        _watch = watch;
        StopWatching();
        if (watch) Watch();
    }

    void AutoScan()
    {
        var folders = WithCategoryFolders(_autoFolders);
        if (_scan != null || folders.Count == 0) return;
        var now = DateTime.UtcNow;
        if (_watch && _watched.Count < folders.Count) Watch(); // a folder (network share) that wasn't there before
        // A first scan a minute after the app starts (catches what changed while it was closed), then on the schedule.
        bool due = _lastScan is { } last
            ? _autoMinutes > 0 && now - last >= TimeSpan.FromMinutes(_autoMinutes)
            : (_autoMinutes > 0 || _watch) && now - _started >= FirstScan;
        bool changed = _dirty && now - _lastChange >= WatchQuiet;
        if (due || changed) _ = ScanAsync(_autoFolders, automatic: true);
    }

    void Watch()
    {
        foreach (var folder in WithCategoryFolders(_autoFolders))
        {
            if (_watched.Contains(folder) || !Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => Touched(e.FullPath);
                watcher.Deleted += (_, e) => Touched(e.FullPath);
                watcher.Changed += (_, e) => Touched(e.FullPath);
                watcher.Renamed += (_, e) => { Touched(e.OldFullPath); Touched(e.FullPath); };
                watcher.Error += (_, _) => Touched(null); // too many changes at once: scan anyway
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _watched.Add(folder);
            }
            catch (Exception ex)
            {
                // E.g. Linux's limit on watched folders: the schedule still picks changes up.
                AppLog.Write($"Library: can't watch {folder} for changes ({ex.Message})");
                _watched.Add(folder);
            }
        }
    }

    /// <summary>A file or folder changed (null: unknown, after the watcher lost track). Called on watcher threads.</summary>
    void Touched(string? path)
    {
        // Audio files, and folders (moved or deleted folders have no extension here); not the many other files.
        if (path != null && Path.HasExtension(path) && !LibraryScanner.IsAudio(path)) return;
        _lastChange = DateTime.UtcNow;
        _dirty = true;
    }

    void StopWatching()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _watched.Clear();
    }

    /// <summary>Scans the music folders into the library in the background, reporting in the status bar.</summary>
    /// <param name="onlyThese">Only these folders (a category's), not every category's folder too.</param>
    public async Task ScanAsync(IReadOnlyList<string> folders, bool automatic = false, bool onlyThese = false)
    {
        if (Db is not { } db || _scan != null) return;
        folders = onlyThese ? folders.Where(SharedPaths.DirectoryExists).ToList() : WithCategoryFolders(folders);
        if (folders.Count == 0)
        {
            if (!automatic) Message?.Invoke(L.T("Choose the music folders to scan in Settings → Library"));
            return;
        }
        _scan = new CancellationTokenSource();
        _dirty = false;
        Changed?.Invoke();
        var token = _scan.Token;
        // Scheduled and automatic scans stay quiet unless they find something.
        if (!automatic) Message?.Invoke(L.T("Library scan: looking for files…"));
        var last = DateTime.MinValue;
        var progress = new Progress<ScanProgress>(p =>
        {
            if ((DateTime.UtcNow - last).TotalSeconds < 0.5 || (automatic && p.Read == 0)) return;
            last = DateTime.UtcNow;
            Message?.Invoke(p.Read == 0
                ? L.F("Library scan: {0:N0} files found, {1:N0} new or changed", p.Found, p.ToRead)
                : L.F("Library scan: reading tags, {0:N0} of {1:N0}", p.Read, p.ToRead));
        });
        try
        {
            var r = await Task.Run(() => LibraryScanner.Scan(db, folders, progress, token));
            // Folders given a category: the songs found there that have none yet get it. The deepest folders first, so
            // Music/80s gives its songs 80s before Music gives the rest of them Music.
            await Task.Run(() =>
            {
                foreach (var (folder, id) in db.FolderCategories().OrderByDescending(r => r.Key.Length))
                    db.SetFolderCategory(folder, id, onlyWithout: true);
            });
            var parts = new List<string> { L.F("{0:N0} added", r.Added), L.F("{0:N0} updated", r.Updated) };
            if (r.Moved > 0) parts.Add(L.F("{0:N0} moved", r.Moved));
            if (r.Removed > 0) parts.Add(L.F("{0:N0} removed (no longer on disk)", r.Removed));
            if (r.SkippedFolders.Count > 0) parts.Add(L.F("skipped {0} (not found)", string.Join(", ", r.SkippedFolders)));
            bool found = r.Added + r.Updated + r.Moved + r.Removed > 0;
            if (!automatic || found || r.Cancelled)
                Message?.Invoke(L.F(automatic ? r.Cancelled ? "Library auto scan stopped: {0}" : "Library auto scan done: {0}"
                    : r.Cancelled ? "Library scan stopped: {0}" : "Library scan done: {0}", string.Join(", ", parts)));
        }
        catch (OperationCanceledException)
        {
            Message?.Invoke(L.T("Library scan stopped"));
        }
        catch (Exception ex)
        {
            Message?.Invoke(L.F("Library scan failed: {0}", ex.Message));
        }
        finally
        {
            _scan = null;
            _lastScan = DateTime.UtcNow;
            Changed?.Invoke();
        }
    }

    public void CancelScan() => _scan?.Cancel();


    // ───────────────────────── Cue points ─────────────────────────

    CueStore? _cues;
    Action<IReadOnlyList<string>>? _cuesChanged;
    readonly Dictionary<string, CuePoints> _pendingCues = new(PathComparer); // edited here, not in the library yet
    string? _cueSince; // server time of the last sync, less a margin; null = read them all
    DateTime _cueSynced = DateTime.MinValue;

    // ───────────────────────── Changes made elsewhere ─────────────────────────

    static readonly TimeSpan StampInterval = TimeSpan.FromSeconds(30);
    DateTime _stampRead = DateTime.UtcNow;
    string? _stamp;
    bool _readingStamp;

    /// <summary>
    /// Categories or songs changed by another program or computer (Sintoniza Import, a scan elsewhere, Pro): those
    /// showing the library read it again. Checked every 30 seconds, in the background.
    /// </summary>
    /// <param name="notify">false: only take note of how the library is now (its own change, or just opened).</param>
    void ReadStamp(LibraryDb db, bool notify = true)
    {
        if (_readingStamp) return;
        _readingStamp = true;
        _stampRead = DateTime.UtcNow;
        Task.Run(db.ChangeStamp).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _readingStamp = false;
            if (!t.IsCompletedSuccessfully || db != Db) return;
            bool changed = notify && _stamp != null && _stamp != t.Result;
            _stamp = t.Result;
            if (changed) Changed?.Invoke();
        }));
    }
    bool _syncing;
    string? _lastCueError;

    /// <summary>
    /// Keeps <paramref name="cues"/> in step with the library: edits made here are saved to it, and edits made in
    /// other studios are fetched every 30 seconds and passed to <paramref name="changed"/> (on the UI thread).
    /// </summary>
    public void AttachCues(CueStore cues, Action<IReadOnlyList<string>> changed)
    {
        _cues = cues;
        _cuesChanged = changed;
        cues.Edited += (path, points) =>
        {
            lock (_pendingCues) _pendingCues[path] = points;
            if (Db is { } db) Queue(() => SaveCues(db, path, points));
        };
    }

    /// <summary>Saves one file's cue points to the library, adding the file to it if needed. Runs in the write queue.</summary>
    void SaveCues(LibraryDb db, string path, CuePoints cues)
    {
        try
        {
            // A file outside the music folders is added to the library, so its cue points are shared too. One that
            // can't be added (not audio, path too long) keeps them on this computer only.
            if (!db.SetCues(path, cues) && !cues.IsEmpty)
            {
                db.Refresh(path);
                db.SetCues(path, cues);
            }
            lock (_pendingCues)
                if (_pendingCues.TryGetValue(path, out var pending) && pending == cues) _pendingCues.Remove(path);
            _lastCueError = null;
        }
        catch (Exception ex)
        {
            // Kept in the pending list: tried again at the next sync.
            if (_lastCueError != ex.Message) Post(L.F("Cue points not saved to the library yet: {0}", ex.Message));
            _lastCueError = ex.Message;
        }
    }

    /// <summary>
    /// Saves pending edits, then fetches the cue points set in other studios since the last sync (all of them
    /// after opening; then cue points that only this computer has are added to the library).
    /// </summary>
    void SyncCues()
    {
        if (_cues is not { } store || Db is not { } db || _syncing) return;
        _syncing = true;
        _cueSynced = DateTime.UtcNow;
        string? since = _cueSince;
        Task.Run(() =>
        {
            List<(string, CuePoints)> pending;
            lock (_pendingCues) pending = _pendingCues.Select(kv => (kv.Key, kv.Value)).ToList();
            foreach (var (path, cues) in pending) SaveCues(db, path, cues);

            string now = db.ServerTime();
            var changes = db.CueChanges(since);
            if (since == null)
            {
                // Cue points set here before the library had them (or while it couldn't be reached): add them.
                var known = changes.Select(c => c.Path).ToHashSet(PathComparer);
                foreach (var (path, cues) in store.All().Where(c => !known.Contains(c.Path))) SaveCues(db, path, cues);
            }
            HashSet<string> keep;
            lock (_pendingCues) keep = _pendingCues.Keys.ToHashSet(PathComparer);
            var changed = store.Apply(changes, keep);
            // Edits stamped just before "now" may commit after this read: look a little further back next time.
            var next = DateTime.Parse(now, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return (changed, since: LibraryDb.Time(next.AddSeconds(-30)));
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _syncing = false;
            if (!t.IsCompletedSuccessfully)
            {
                string error = t.Exception?.InnerException?.Message ?? L.T("unknown error");
                if (_lastCueError != error) Message?.Invoke(L.F("Cue points not synced with the library: {0}", error));
                _lastCueError = error;
                return;
            }
            if (db != Db) return; // reopened meanwhile
            _cueSince = t.Result.since;
            if (t.Result.changed.Count > 0)
            {
                AppLog.Write($"Cue points of {t.Result.changed.Count} file(s) changed in the library");
                _cuesChanged?.Invoke(t.Result.changed);
            }
        }));
    }

    void Post(string message) => Dispatcher.UIThread.Post(() => Message?.Invoke(message));
}
