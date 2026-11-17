using System.Globalization;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Online at work: streams with <see cref="OnlineStreamer"/>, keeps the day's events (a file a day, kept 90
/// days) and the day's figures (time online and off, connections lost, the longest time off, listeners), and sends
/// alerts: off the air, on the backup server, silence, no sound from the source, "still online" once a day. Ticked once
/// a second by the window (on its thread).
/// </summary>
sealed class OnlineService : IDisposable
{
    const int KeepLogDays = 90;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    OnlineSettings _s;
    OnlineStreamer? _streamer;
    DateTime _day = StationTime.Today, _aliveSent = DateTime.MinValue, _nextClean = DateTime.MinValue, _nextListeners = DateTime.MinValue, _lastTick = StationTime.Now;
    DateTime? _offSince, _silentSince;
    bool _offReported, _backupReported, _silenceReported, _inputReported, _wasOnline, _dropped;
    static readonly object LogLock = new();
    int _listening;

    public OnlineService(OnlineSettings settings)
    {
        _s = settings;
        // "Still online" today too, unless its hour has gone by already.
        _aliveSent = StationTime.Now.Hour > (_s.AliveHour ?? 23) ? StationTime.Today : StationTime.Today.AddDays(-1);
    }

    /// <summary>What happened, for the window (already in the day's events).</summary>
    public event Action<string>? Message;

    public bool IsStreaming => _streamer?.IsRunning == true;
    public OnlineStreamer? Streamer => _streamer;

    /// <summary>Today's figures.</summary>
    public double OnlineSeconds { get; private set; }
    public double OffSeconds { get; private set; }
    public int Drops { get; private set; }
    public double LongestOff { get; private set; }
    /// <summary>Listeners now (from the server's statistics), and the most today; null: not known.</summary>
    public int? Listeners { get; private set; }
    public int? MostListeners { get; private set; }
    /// <summary>Since when it has been off the air while it should be on (null: on air, or stopped).</summary>
    public DateTime? OffSince => _offSince;

    public OnlineSettings Settings
    {
        get => _s;
        set
        {
            bool restart = IsStreaming && !value.SameStream(_s);
            _s = value;
            if (restart)
            {
                Note(L.T("Settings changed: streaming again with them"));
                Stop(quiet: true);
                Start();
            }
            else _streamer?.SetMonitor(_s.Monitor ? _s.MonitorOutput : null);
        }
    }

    public void Start()
    {
        if (IsStreaming) return;
        if (!_s.HasServer)
        {
            Note(L.T("Set the server first (Settings → Server)"));
            return;
        }
        try
        {
            var streamer = new OnlineStreamer(_s.ToOptions());
            streamer.Log += Note;
            streamer.Start();
            streamer.SetMonitor(_s.Monitor ? _s.MonitorOutput : null);
            _streamer = streamer;
            _offSince = StationTime.Now;
            _silentSince = null;
            _offReported = _backupReported = _silenceReported = _inputReported = _wasOnline = _dropped = false;
        }
        catch (Exception ex)
        {
            Note(L.F("Can't stream: {0}", ex.Message));
            Alert("stream", false, L.F("can't stream: {0}", ex.Message));
        }
    }

    /// <param name="quiet">Not an alert (settings changed, the program closing after asking).</param>
    public void Stop(bool quiet = false)
    {
        if (_streamer is not { } s) return;
        _streamer = null;
        s.Stop();
        s.Dispose();
        if (!quiet && _s.Alerts.HasChannel) Alert("stream", false, L.T("streaming stopped by hand"));
        _offSince = null;
        Listeners = null;
    }

    public void SetMonitor(bool on)
    {
        _s.Monitor = on;
        _streamer?.SetMonitor(on ? _s.MonitorOutput : null);
    }

    public (float Left, float Right) TakePeaks() => _streamer?.TakePeaks() ?? (0, 0);

    // ───────────────────────── The day's events ─────────────────────────

    /// <summary>A folder of a file a day: 2026-11-17.log.</summary>
    public static string LogFolder => Path.Combine(AppSettings.DataFolder, "online-log");

    static string LogFile(DateTime day) => Path.Combine(LogFolder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>Kept in the day's events and said to the window (from any thread).</summary>
    public void Note(string message)
    {
        var now = StationTime.Now;
        try
        {
            Directory.CreateDirectory(LogFolder);
            lock (LogLock) File.AppendAllText(LogFile(now), $"{now:HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* the window still shows it */ }
        AppLog.Write($"{AppInfo.OnlineName}: {message}");
        Message?.Invoke(message);
    }

    /// <summary>A day's events, the latest first.</summary>
    public static List<string> Events(DateTime day)
    {
        try
        {
            string file = LogFile(day);
            if (!File.Exists(file)) return [];
            var lines = File.ReadAllLines(file).Where(l => l.Length > 0).ToList();
            lines.Reverse();
            return lines;
        }
        catch { return []; }
    }

    // ───────────────────────── Every second ─────────────────────────

    public void Tick()
    {
        var now = StationTime.Now;
        if (now.Date != _day)
        {
            _day = now.Date;
            OnlineSeconds = OffSeconds = LongestOff = 0;
            Drops = 0;
            MostListeners = Listeners;
        }
        double seconds = Math.Clamp((now - _lastTick).TotalSeconds, 0, 5);
        _lastTick = now;
        if (_streamer is { } s) Watch(s, now, seconds);
        if (now >= _nextClean)
        {
            _nextClean = now.AddHours(6);
            Task.Run(Clean);
        }
        if (_s.AliveHour is { } hour && now.Hour == hour && _aliveSent < now.Date)
        {
            _aliveSent = now.Date;
            Alert("alive", true, _streamer?.State == OnlineState.Online
                ? L.F("still online ({0}, {1} listening)", _streamer.Server.Address, Listeners?.ToString(CultureInfo.InvariantCulture) ?? "?")
                : L.T("running, but NOT online"));
        }
    }

    void Watch(OnlineStreamer s, DateTime now, double seconds)
    {
        bool online = s.State == OnlineState.Online;
        if (online)
        {
            OnlineSeconds += seconds;
            if (_offSince is { } since)
            {
                double off = (now - since).TotalSeconds;
                if (_dropped) LongestOff = Math.Max(LongestOff, off); // a connection lost, not the first one being made
                if (_offReported) Alert("stream", true, L.F("online again ({0}, off for {1})", s.Server.Address, Format.Duration(off)));
            }
            _offSince = null;
            _offReported = false;
            _dropped = false;
        }
        else
        {
            OffSeconds += seconds;
            if (_wasOnline)
            {
                Drops++;
                _dropped = true;
            }
            _offSince ??= now;
            if (!_offReported && (now - _offSince.Value).TotalSeconds >= Math.Max(5, _s.OfflineAlertSeconds))
            {
                _offReported = true;
                Alert("stream", false, L.F("OFF THE AIR on the internet for {0:0} s: {1}", (now - _offSince.Value).TotalSeconds, s.Error ?? L.T("connecting")));
            }
        }
        _wasOnline = online;

        // On the backup server
        bool backup = online && s.OnBackup;
        if (backup && !_backupReported) { _backupReported = true; Alert("backup", false, L.F("on the backup server {0}: the main one doesn't answer", s.Server.Address)); }
        else if (online && !backup && _backupReported) { _backupReported = false; Alert("backup", true, L.T("back on the main server")); }

        // No sound from the source at all (an input unplugged, a relayed stream that dropped)
        bool noInput = s.SecondsWithoutData > 10;
        if (noInput && !_inputReported)
        {
            _inputReported = true;
            Alert("input", false, L.F("no sound from {0}", s.Source));
        }
        else if (!noInput && _inputReported)
        {
            _inputReported = false;
            Alert("input", true, L.F("sound from {0} again", s.Source));
        }

        // Silence
        bool quiet = s.SilentSeconds >= 2;
        if (quiet) _silentSince ??= now.AddSeconds(-s.SilentSeconds);
        else
        {
            if (_silenceReported) Alert("silence", true, L.T("sound again"));
            _silentSince = null;
            _silenceReported = false;
        }
        if (_silentSince is { } silent && !_silenceReported && (now - silent).TotalSeconds >= Math.Max(5, _s.SilenceSeconds))
        {
            _silenceReported = true;
            Note(L.F("Silence for {0:0} s", (now - silent).TotalSeconds));
            Alert("silence", false, L.F("silence for {0:0} s{1}", (now - silent).TotalSeconds, s.OnFallback ? " " + L.T("(the fallback file is on)") : ""));
        }

        // Listeners, every minute while online
        if (online && now >= _nextListeners && Interlocked.CompareExchange(ref _listening, 1, 0) == 0)
        {
            _nextListeners = now.AddMinutes(1);
            var server = s.Server;
            _ = Task.Run(async () =>
            {
                try { if (await ReadListeners(server) is { } n) Dispatch(() => { Listeners = n; MostListeners = Math.Max(MostListeners ?? 0, n); }); }
                finally { Volatile.Write(ref _listening, 0); }
            });
        }
        if (!online) Listeners = null;
    }

    /// <summary>Done on the window's thread (the figures are read there).</summary>
    static void Dispatch(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a);

    /// <summary>The listeners on the mount, from the server's public statistics; null: not known.</summary>
    static async Task<int?> ReadListeners(OnlineServer server)
    {
        var mounts = ListenerStats.MountList("", server.Shoutcast ? "*" : server.Address[server.Address.IndexOf('/')..]);
        foreach (var page in ListenerStats.Pages($"http://{server.Host}:{server.Port}", server.Shoutcast))
            try
            {
                string body = await Http.GetStringAsync(page);
                if (ListenerStats.Parse(body, mounts) is { Count: { } n }) return n;
            }
            catch { /* the next page, or not known */ }
        return null;
    }

    /// <summary>The day files older than kept.</summary>
    static void Clean()
    {
        try
        {
            if (!Directory.Exists(LogFolder)) return;
            var before = StationTime.Today.AddDays(-KeepLogDays);
            foreach (var file in Directory.GetFiles(LogFolder, "*.log"))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < before)
                    File.Delete(file);
        }
        catch { /* tried again later */ }
    }

    void Alert(string kind, bool ok, string message)
    {
        if (!_s.Alerts.HasChannel) return;
        string station = _s.StationName.Length > 0 ? $"{_s.StationName} · {AppInfo.OnlineName}" : AppInfo.OnlineName;
        _ = AlertSender.SendAsync(_s.Alerts, station, kind, ok, message).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result.Length > 0) Note(L.F("Alert not sent: {0}", t.Result));
        });
    }

    public void Dispose() => Stop(quiet: true);
}
