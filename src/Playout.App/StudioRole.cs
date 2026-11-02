using Playout.Library;

namespace Playout.App;

/// <summary>
/// Several computers running Sintoniza Emi for one station (a shared MySQL / MariaDB library): one is on air, the others
/// are standby, ready and silent. Every couple of seconds this computer says it is running (the studios table) and
/// reads which one is on air. The first to start goes on air; a standby goes on air when asked (Take on air) or by
/// itself when the one on air hasn't answered for a while (Settings). A computer that finds another on air in its place
/// goes standby. With a library of its own (SQLite), or while the database can't be reached, nothing changes.
/// </summary>
sealed class StudioRole : IDisposable
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    readonly LibraryService _library;
    readonly Func<string> _playing;
    readonly Func<(bool Auto, int Seconds)> _takeOver;
    Thread? _thread;
    volatile bool _stopped;
    /// <summary>When this computer last told the database it is running (for the log when that was held up).</summary>
    DateTime? _lastSeen;
    DateTime _lastProblemLogged;
    string? _problem;
    /// <summary>How long it had been, at this check, since the one before got through.</summary>
    TimeSpan _gap;
    readonly object _gate = new(); // one look at the database, or one Take on air, at a time
    readonly TaskCompletionSource _known = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StudioRole(LibraryService library, bool standby, Func<string> playing, Func<(bool Auto, int Seconds)> takeOver)
    {
        _library = library;
        Standby = standby;
        _playing = playing;
        _takeOver = takeOver;
    }

    /// <summary>
    /// Starts looking (once <see cref="Changed"/> has its handler, so the first answer isn't missed). On a thread of its
    /// own, not the shared pool: nothing else the program does can hold it up long enough for a standby computer to
    /// think this one stopped answering.
    /// </summary>
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(() =>
        {
            while (!_stopped)
            {
                var started = DateTime.UtcNow;
                Check();
                var wait = Every - (DateTime.UtcNow - started);
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            }
        })
        { IsBackground = true, Name = "Studios", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>For the log: how a check went (slow, failing, or back), at most every 30 s while it lasts.</summary>
    void Note(string? problem, TimeSpan took)
    {
        var now = DateTime.UtcNow;
        if (problem == null)
        {
            // The first after starting has nothing to compare with.
            var silent = _gap = _lastSeen is { } last ? now - last : TimeSpan.Zero;
            _lastSeen = now;
            if (_problem != null || silent > TimeSpan.FromSeconds(6))
                AppLog.Write(L.F("Studios: the database knows this computer is running again (it hadn't heard from it for {0:0} s)", silent.TotalSeconds));
            if (took > TimeSpan.FromSeconds(4)) AppLog.Write(L.F("Studios: telling the database this computer is running took {0:0.0} s", took.TotalSeconds));
            _problem = null;
            return;
        }
        if (problem == _problem && now - _lastProblemLogged < TimeSpan.FromSeconds(30)) return;
        _problem = problem;
        _lastProblemLogged = now;
        AppLog.Write(L.F("Studios: can't tell the database this computer is running ({0}); {1:0} s since it last could", problem, (now - (_lastSeen ?? now)).TotalSeconds));
    }

    /// <summary>Done once this computer knows whether it is on air: read from the database, or a library of its own.</summary>
    public Task Known => _known.Task;

    /// <summary>Another computer is on air.</summary>
    public bool Standby { get; private set; }

    /// <summary>The studios as last read (null: none yet, or not shared).</summary>
    public StudioList? Studios { get; private set; }

    /// <summary>This computer goes on air, or standby (with why, for the log); raised off the UI thread.</summary>
    public event Action<bool, string>? Changed;

    /// <summary>The studios were read (for the banner); raised off the UI thread.</summary>
    public event Action? Seen;

    static string Me => AppInfo.ComputerName;

    /// <summary>The library is shared by several computers (MySQL / MariaDB).</summary>
    bool Shared => _library.Db is { IsMySql: true };

    /// <summary>Take on air: this computer goes on air now, the one on air goes standby.</summary>
    public Task TakeOnAir() => Task.Run(() =>
    {
        lock (_gate)
        {
            if (_library.Db is not { } db) throw new InvalidOperationException(L.T("The library can't be reached"));
            db.TakeOnAir(StationStore.Station, Me);
            Set(false, L.T("This computer was put on air"));
            Read(db);
        }
    });

    void Check()
    {
        if (!Monitor.TryEnter(_gate)) return; // the last look (or Take on air) isn't over yet
        try
        {
            if (!Shared || _library.Db is not { } db)
            {
                // A library of its own: the only studio. The shared one away: as it was (never silence the station
                // because of the database; a standby stays standby).
                if (_library.Db is { IsMySql: false })
                {
                    Studios = null;
                    Set(false, "");
                    _known.TrySetResult();
                }
                return;
            }
            var started = DateTime.UtcNow;
            try { db.StudioSeen(StationStore.Station, Me, _playing(), Updates.CurrentText); }
            catch (Exception ex)
            {
                Note(ex.Message, DateTime.UtcNow - started);
                throw;
            }
            Note(null, DateTime.UtcNow - started);
            var list = Read(db);
            var onAir = list.OnAir;
            var (auto, seconds) = _takeOver();
            if (onAir == null)
            {
                if (db.TakeOnAir(StationStore.Station, Me, TimeSpan.FromSeconds(Math.Max(5, seconds))))
                    Set(false, L.T("On air: no other computer was"));
            }
            else if (onAir.Computer == Me) Set(false, "");
            else if (auto && list.Silent(onAir) >= TimeSpan.FromSeconds(Math.Max(5, seconds)))
            {
                if (db.TakeOnAir(StationStore.Station, Me, TimeSpan.FromSeconds(Math.Max(5, seconds))))
                    Set(false, L.F("On air: {0} stopped answering {1:0} s ago", onAir.Computer, list.Silent(onAir).TotalSeconds));
            }
            else
            {
                // Put standby here by another computer while this one was on air: say how long this one had been silent.
                // A long gap: it took over because this one went silent. Otherwise someone put it on air there.
                Set(true, Standby ? L.F("Standby: {0} is on air", onAir.Computer)
                    : _gap > TimeSpan.FromSeconds(6) ? L.F("Standby: {0} took over; this computer hadn't reached the database for {1:0} s", onAir.Computer, _gap.TotalSeconds)
                    : L.F("Standby: {0} was put on air", onAir.Computer));
            }
            Read(db);
            _known.TrySetResult();
        }
        catch (Exception)
        {
            // The database away for a moment: as it was.
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    StudioList Read(LibraryDb db)
    {
        var list = db.Studios(StationStore.Station);
        Studios = list;
        Seen?.Invoke();
        return list;
    }

    void Set(bool standby, string why)
    {
        if (Standby == standby) return;
        Standby = standby;
        Changed?.Invoke(standby, why);
    }

    public void Dispose() => _stopped = true;
}
