using System.Runtime.InteropServices;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// The items about to play read into memory ahead of time, off the engine's thread, so a file on a network share plays
/// on whatever the network does: once an item is in memory, a share that stops answering for a while (Wi-Fi, a server
/// restarting) is never heard. A file too large, or not read in time, plays from the file as before.
/// </summary>
sealed class FileCache
{
    /// <summary>Larger files (a long programme) play from the file.</summary>
    const long MaxBytes = 80L * 1024 * 1024;
    const int Chunk = 1 << 20;

    /// <summary>At most two files read at a time: a share that stops answering never ties up more than that.</summary>
    static readonly SemaphoreSlim Readers = new(2);

    sealed record Entry(Task<byte[]?> Load, CancellationTokenSource Stop);

    readonly object _gate = new();
    readonly Dictionary<string, Entry> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts reading <paramref name="path"/> (a local path) unless it is read or being read; <paramref name="done"/>
    /// when a read still to finish is over, whether it is in memory then or not (too large, not readable). A read
    /// already over calls nothing: the caller sees it with <see cref="Ready"/>.
    /// </summary>
    public void Prefetch(string path, Action? done = null)
    {
        Task<byte[]?> load;
        lock (_gate)
        {
            if (_files.TryGetValue(path, out var known)) load = known.Load;
            else
            {
                var stop = new CancellationTokenSource();
                load = Task.Factory.StartNew(() => Read(path, stop.Token), stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                _files[path] = new Entry(load, stop);
            }
        }
        if (done != null && !load.IsCompleted) load.ContinueWith(_ => done(), TaskScheduler.Default);
    }

    static byte[]? Read(string path, CancellationToken stop)
    {
        try { Readers.Wait(stop); }
        catch (OperationCanceledException) { return null; }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes) return null;
            var bytes = new byte[info.Length];
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            int got = 0;
            while (got < bytes.Length)
            {
                // Not wanted any more (played, taken out): let go, not read on for nothing.
                if (stop.IsCancellationRequested) return null;
                int n = file.Read(bytes, got, Math.Min(Chunk, bytes.Length - got));
                if (n == 0) return null; // shorter than it said: not trusted, it plays from the file
                got += n;
            }
            return bytes;
        }
        catch (Exception) { return null; } // not there or not readable now: it plays from the file (and says why then)
        finally { Readers.Release(); }
    }

    /// <summary>The file's bytes if it has been read already (never waits).</summary>
    public byte[]? Ready(string path)
    {
        lock (_gate)
            return _files.TryGetValue(path, out var entry) && entry.Load.IsCompletedSuccessfully ? entry.Load.Result : null;
    }

    /// <summary>Forgets what isn't one of <paramref name="paths"/> (played, or taken out of the playlist), stopping its read.</summary>
    public void Keep(IReadOnlyCollection<string> paths)
    {
        lock (_gate)
            foreach (var path in _files.Keys.Where(p => !paths.Contains(p)).ToList())
                Drop(path);
    }

    /// <summary>Forgets one file (its tags are being written: what was read is out of date).</summary>
    public void Forget(string path)
    {
        lock (_gate) Drop(path);
    }

    void Drop(string path)
    {
        if (!_files.Remove(path, out var entry)) return;
        entry.Stop.Cancel();
    }

    // ───── A stream from memory: the bytes pinned while BASS reads them, let go when the stream is freed ─────

    static readonly Dictionary<int, GCHandle> Pinned = new();
    static readonly SyncProcedure Unpin = (_, channel, _, _) =>
    {
        lock (Pinned)
            if (Pinned.Remove(channel, out var pin)) pin.Free();
    };

    /// <summary>A decoding stream over <paramref name="bytes"/>; 0 if BASS can't (the caller opens the file instead).</summary>
    public static int Open(byte[] bytes, BassFlags flags)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        int handle = Bass.CreateStream(pin.AddrOfPinnedObject(), 0, bytes.LongLength, flags);
        if (handle == 0)
        {
            pin.Free();
            return 0;
        }
        lock (Pinned) Pinned[handle] = pin;
        if (Bass.ChannelSetSync(handle, SyncFlags.Free, 0, Unpin) == 0)
        {
            // No way to know when it is freed: not used (never memory pinned for good, nor freed under BASS).
            Bass.StreamFree(handle);
            lock (Pinned)
                if (Pinned.Remove(handle, out var left)) left.Free();
            return 0;
        }
        return handle;
    }
}
