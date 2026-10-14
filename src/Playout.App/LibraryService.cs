using Avalonia.Threading;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The app's connection to the music library: opens it (in the background: a MySQL server can take a while to
/// answer), runs scans, and writes the play history in order, off the UI and engine threads.
/// </summary>
sealed class LibraryService
{
    Task _writes = Task.CompletedTask;
    int _openVersion;
    CancellationTokenSource? _scan;

    /// <summary>The open library, or null (not set up yet, opening, or it failed: see <see cref="Error"/>).</summary>
    public LibraryDb? Db { get; private set; }
    public string? Error { get; private set; }
    public bool IsScanning => _scan != null;

    /// <summary>Raised on the UI thread when the library was (re)opened, or its contents changed (scan, edits).</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread with a line for the status bar and the log.</summary>
    public event Action<string>? Message;

    public async Task OpenAsync(LibrarySettings settings)
    {
        int version = ++_openVersion;
        Db = null;
        Error = null;
        Changed?.Invoke();
        var copy = settings.Clone();
        try
        {
            var db = await Task.Run(() => LibraryDb.Open(copy));
            if (version != _openVersion) return; // settings changed again meanwhile
            Db = db;
        }
        catch (Exception ex)
        {
            if (version != _openVersion) return;
            Error = $"Library unavailable ({copy.Describe()}): {ex.Message}";
            Message?.Invoke(Error);
        }
        Changed?.Invoke();
    }

    /// <summary>Tells the views the contents changed (after an edit).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Adds an item that went on air to the history. Safe from any thread; written in order.</summary>
    public void RecordPlay(Track track, string source) => RecordPlay(track.Path, track.Artist, track.Title, source);

    public void RecordPlay(string path, string artist, string title, string source)
    {
        if (Db is not { } db) return;
        var at = DateTime.UtcNow;
        lock (this)
            _writes = _writes.ContinueWith(_ =>
            {
                try { db.RecordPlay(path, artist, title, at, source); }
                catch (Exception ex) { Post($"Play history not saved: {ex.Message}"); }
            }, TaskScheduler.Default);
    }

    /// <summary>Re-reads a file's tags into the library (after editing them).</summary>
    public void Refresh(string path)
    {
        if (Db is not { } db) return;
        Task.Run(() =>
        {
            try { db.Refresh(path); }
            catch (Exception ex) { Post($"Library not updated: {ex.Message}"); return; }
            Dispatcher.UIThread.Post(() => Changed?.Invoke());
        });
    }

    /// <summary>Scans the music folders into the library in the background, reporting in the status bar.</summary>
    public async Task ScanAsync(IReadOnlyList<string> folders)
    {
        if (Db is not { } db || _scan != null) return;
        if (folders.Count == 0)
        {
            Message?.Invoke("Choose the music folders to scan in Settings → Library");
            return;
        }
        _scan = new CancellationTokenSource();
        var token = _scan.Token;
        Message?.Invoke("Library scan: looking for files…");
        var last = DateTime.MinValue;
        var progress = new Progress<ScanProgress>(p =>
        {
            if ((DateTime.UtcNow - last).TotalSeconds < 0.5) return;
            last = DateTime.UtcNow;
            Message?.Invoke(p.Read == 0
                ? $"Library scan: {p.Found:N0} files found, {p.ToRead:N0} new or changed"
                : $"Library scan: reading tags, {p.Read:N0} of {p.ToRead:N0}");
        });
        try
        {
            var r = await Task.Run(() => LibraryScanner.Scan(db, folders, progress, token));
            var parts = new List<string> { $"{r.Added:N0} added", $"{r.Updated:N0} updated" };
            if (r.Moved > 0) parts.Add($"{r.Moved:N0} moved");
            if (r.Missing > 0) parts.Add($"{r.Missing:N0} missing");
            if (r.SkippedFolders.Count > 0) parts.Add($"skipped {string.Join(", ", r.SkippedFolders)} (not found)");
            Message?.Invoke($"Library scan {(r.Cancelled ? "stopped" : "done")}: {string.Join(", ", parts)}");
        }
        catch (OperationCanceledException)
        {
            Message?.Invoke("Library scan stopped");
        }
        catch (Exception ex)
        {
            Message?.Invoke($"Library scan failed: {ex.Message}");
        }
        finally
        {
            _scan = null;
            Changed?.Invoke();
        }
    }

    public void CancelScan() => _scan?.Cancel();

    void Post(string message) => Dispatcher.UIThread.Post(() => Message?.Invoke(message));
}
