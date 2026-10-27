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
    readonly Timer _timer;
    int _busy;

    public StudioRole(LibraryService library, bool standby, Func<string> playing, Func<(bool Auto, int Seconds)> takeOver)
    {
        _library = library;
        Standby = standby;
        _playing = playing;
        _takeOver = takeOver;
        _timer = new Timer(_ => Check(), null, TimeSpan.Zero, Every);
    }

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
        if (_library.Db is not { } db) throw new InvalidOperationException(L.T("The library can't be reached"));
        db.TakeOnAir(StationStore.Station, Me);
        Set(false, L.T("This computer was put on air"));
        Read(db);
    });

    void Check()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!Shared || _library.Db is not { } db)
            {
                // A library of its own: the only studio. The shared one away: as it was (never silence the station
                // because of the database; a standby stays standby).
                if (_library.Db is { IsMySql: false }) { Studios = null; Set(false, ""); }
                return;
            }
            db.StudioSeen(StationStore.Station, Me, _playing(), Updates.CurrentText);
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
            else Set(true, L.F("Standby: {0} is on air", onAir.Computer));
            Read(db);
        }
        catch (Exception)
        {
            // The database away for a moment: as it was.
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
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

    public void Dispose() => _timer.Dispose();
}
