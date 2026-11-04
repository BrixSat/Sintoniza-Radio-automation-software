using System.Text;
using System.Text.Json;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sends this computer's log to the library (Sintoniza Pro → Station → Studios), so what happened in a studio can be
/// read from anywhere: every 15 s the new whole lines of the day's file (all that wrote there: the on-air program, its
/// watchdog, Rec and Pro on this computer), the problems marked. Where it got to is kept, so lines written while the
/// database was away are sent once it is back, and none twice. Runs on a timer of its own, never the screen's.
/// </summary>
sealed class LogShipper : IDisposable
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(15);
    /// <summary>Tries at the same place before what is there is passed over (said in the log), never blocking what comes after.</summary>
    const int Tries = 4;

    readonly Func<LibraryDb?> _db;
    readonly string _stateFile;
    int _failures;
    readonly Timer _timer;
    int _busy;
    DateTime _nextPrune = DateTime.MinValue;
    DateTime _lastAt;

    sealed record Sent(string Day, long Offset);

    /// <param name="key">The library and station it sends to: another one (Settings changed) starts again from today's start.</param>
    public LogShipper(Func<LibraryDb?> db, string key)
    {
        _db = db;
        _stateFile = Path.Combine(AppLog.Folder, $"sent-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12]}.json");
        _timer = new Timer(_ => Ship(), null, TimeSpan.FromSeconds(20), Every);
    }

    void Ship()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (_db() is not { } db) return;
            var today = StationTime.Today;
            var sent = Load() ?? new Sent(today.ToString("yyyy-MM-dd"), 0);
            // Away longer than the library keeps lines: from the first day it still keeps.
            var oldest = today.AddDays(-(LibraryDb.StudioLogKeepDays - 1)).ToString("yyyy-MM-dd");
            if (string.CompareOrdinal(sent.Day, oldest) < 0) sent = new Sent(oldest, 0);
            // The rest of an earlier day first (written before midnight, or while the database was away).
            while (string.CompareOrdinal(sent.Day, today.ToString("yyyy-MM-dd")) < 0)
            {
                if (!Send(db, sent, out _)) return;
                var next = DateTime.ParseExact(sent.Day, "yyyy-MM-dd", null).AddDays(1);
                sent = new Sent(next.ToString("yyyy-MM-dd"), 0);
                Save(sent);
            }
            if (Send(db, sent, out long offset)) Save(sent with { Offset = offset });
            _failures = 0;
            if (DateTime.UtcNow >= _nextPrune)
            {
                _nextPrune = DateTime.UtcNow.AddHours(1);
                db.PruneStudioLog(StationTime.Now);
            }
        }
        catch (Exception ex)
        {
            // The database away: tried again in 15 s, from where it got to. The same place failing again and again (a
            // line the database won't take): passed over, and said, so what comes after still goes.
            if (++_failures < Tries) return;
            _failures = 0;
            if (Load() is { } stuck && SkipTo(stuck) is { } past)
            {
                Save(past);
                AppLog.Write($"Studios' log: lines couldn't be sent ({ex.GetBaseException().Message}); passed over");
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>The day's whole lines from <paramref name="sent"/>'s offset on, sent; where the file has been read to.</summary>
    bool Send(LibraryDb db, Sent sent, out long offset)
    {
        offset = sent.Offset;
        string file = Path.Combine(AppLog.Folder, sent.Day + ".log");
        if (!File.Exists(file)) return true;
        byte[] bytes;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length < sent.Offset) offset = 0; // a new file under the same name: from its start
            stream.Seek(offset, SeekOrigin.Begin);
            bytes = new byte[stream.Length - offset];
            int got = 0;
            while (got < bytes.Length && stream.Read(bytes, got, bytes.Length - got) is var n and > 0) got += n;
            Array.Resize(ref bytes, got);
        }
        // Only whole lines: one being written now is sent next time.
        int end = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        if (end == 0) return true;
        var day = DateTime.ParseExact(sent.Day, "yyyy-MM-dd", null);
        var lines = Parse(Encoding.UTF8.GetString(bytes, 0, end), day);
        db.AddStudioLog(StationStore.Station, AppInfo.ComputerName, lines); // all or none: never the same lines twice
        offset += end;
        return true;
    }

    /// <summary>Where the whole lines read from <paramref name="sent"/> end (to pass over what can't be sent).</summary>
    static Sent? SkipTo(Sent sent)
    {
        try
        {
            string file = Path.Combine(AppLog.Folder, sent.Day + ".log");
            if (!File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[Math.Max(0, stream.Length - sent.Offset)];
            stream.Seek(sent.Offset, SeekOrigin.Begin);
            int got = 0;
            while (got < bytes.Length && stream.Read(bytes, got, bytes.Length - got) is var n and > 0) got += n;
            int end = Array.LastIndexOf(bytes, (byte)'\n', Math.Max(0, got - 1)) + 1;
            return end > 0 ? sent with { Offset = sent.Offset + end } : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>The log's entries: a line that starts with a time starts one; the lines after it without one (an error's details) belong to it.</summary>
    List<(DateTime At, bool Problem, string Line)> Parse(string text, DateTime day)
    {
        var entries = new List<(DateTime At, bool Problem, string Line)>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.Length >= 10 && TimeOnly.TryParseExact(line[..8], "HH:mm:ss", out var time))
            {
                _lastAt = day + time.ToTimeSpan();
                string what = line[8..].TrimStart();
                entries.Add((_lastAt, IsProblem(what), what));
            }
            else if (entries.Count > 0)
            {
                var (at, problem, what) = entries[^1];
                if (what.Length < 4000) entries[^1] = (at, problem, what + "\n" + line);
            }
            else entries.Add((_lastAt > day ? _lastAt : day, IsProblem(line), line));
        }
        return entries;
    }

    /// <summary>What marks a problem in the log, as written (in English, and in the language the program writes in).</summary>
    static readonly Lazy<string[]> Markers = new(() =>
    {
        string[] literal =
        [
            "Display: the position isn't moving", "Display: sound is coming out but the player isn't playing", "Watchdog: the app",
            "Error:", "Crash:", "Background error:", "The screen stopped updating", "Screen update failed",
        ];
        string[] keys =
        [
            "Studios: can't tell the database this computer is running ({0}); {1:0} s since it last could", "Alert: {0}",
            "Connection to the streaming server was lost", "Silence on air for {0:0} s", "Engine error: {0}", "Cannot play {0}: {1}; skipping",
            "Could not connect to {0} ({1}); check host, port, mount and password", "Encoder start failed: {0}", "Cart failed: {0} ({1})",
            "Skipped {0}: {1}",
        ];
        static string Before(string text) => text.IndexOf('{') is var i and > 0 ? text[..i] : text;
        return literal.Concat(keys.Select(Before)).Concat(keys.Select(k => Before(L.T(k)))).Where(m => m.Trim().Length > 3).Distinct().ToArray();
    });

    /// <summary>A problem, from how it starts (a song called "Error: …" on air isn't one).</summary>
    public static bool IsProblem(string line) => Markers.Value.Any(m => line.StartsWith(m, StringComparison.Ordinal));

    Sent? Load()
    {
        try { return File.Exists(_stateFile) ? JsonSerializer.Deserialize<Sent>(File.ReadAllText(_stateFile)) : null; }
        catch (Exception) { return null; }
    }

    void Save(Sent sent)
    {
        try { File.WriteAllText(_stateFile, JsonSerializer.Serialize(sent)); }
        catch (Exception) { /* sent again next time: the lines twice rather than none */ }
    }

    public void Dispose() => _timer.Dispose();
}
