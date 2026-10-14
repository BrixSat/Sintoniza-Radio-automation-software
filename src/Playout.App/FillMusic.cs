namespace Playout.App;

/// <summary>
/// Random music from a folder (and its subfolders) for when the playlist runs out in AUTO. Avoids repeating
/// recent picks; the folder is read again every few minutes, in the background, so new files are picked up.
/// </summary>
sealed class FillMusic
{
    static readonly TimeSpan Rescan = TimeSpan.FromMinutes(10);
    static readonly TimeSpan RescanEmpty = TimeSpan.FromSeconds(30); // nothing found (yet): a network share may be back soon

    readonly string _folder;
    readonly Queue<string> _recent = new();
    readonly object _lock = new();
    string[] _files = [];
    DateTime _scanned = DateTime.MinValue;
    Task? _scan;

    public FillMusic(string folder)
    {
        _folder = folder;
        StartScan();
    }

    /// <summary>A file to play, or null if the folder has none, isn't there or hasn't been read yet. Called on the engine thread.</summary>
    public string? Next()
    {
        // Never waits for the folder to be read (this runs on the engine thread): null until it has been,
        // and the engine asks again a few seconds later.
        if (DateTime.UtcNow - _scanned > (_files.Length == 0 ? RescanEmpty : Rescan)) StartScan();

        lock (_lock)
        {
            var files = _files;
            if (files.Length == 0) return null;
            // Not one of the last half of the folder (at most 200), so it doesn't sound like a loop.
            int keep = Math.Min(files.Length / 2, 200);
            while (_recent.Count > keep) _recent.Dequeue();
            var recent = _recent.ToHashSet();
            var choices = files.Where(f => !recent.Contains(f)).ToArray();
            string pick = choices.Length > 0 ? choices[Random.Shared.Next(choices.Length)] : files[Random.Shared.Next(files.Length)];
            _recent.Enqueue(pick);
            return pick;
        }
    }

    void StartScan()
    {
        lock (_lock)
        {
            if (_scan is { IsCompleted: false }) return;
            _scanned = DateTime.UtcNow;
            _scan = Task.Run(() =>
            {
                string[] found;
                try { found = Directory.Exists(_folder) ? AudioFiles.Expand([_folder]).ToArray() : []; }
                catch { found = []; }
                if (found.Length > 0) lock (_lock) _files = found; // a network folder that is briefly away keeps the last list
            });
        }
    }
}
