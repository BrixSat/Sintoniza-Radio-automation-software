using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>An Icecast or Shoutcast server Sintoniza Online sends to.</summary>
/// <param name="User">Icecast's source user (empty: "source", the usual one).</param>
public sealed record OnlineServer(string Host, int Port, string Mount, string User, string Password, bool Shoutcast = false)
{
    public bool IsSet => Host.Trim().Length > 0 && Port is > 0 and < 65536;

    string MountPath => Mount.Trim() is { Length: > 0 } m ? (m.StartsWith('/') ? m : "/" + m) : "/";

    /// <summary>host:port/mount (Icecast) or host:port (Shoutcast): where it connects, for the screen and the log.</summary>
    public string Address => Shoutcast ? $"{Host.Trim()}:{Port}" : $"{Host.Trim()}:{Port}{MountPath}";

    /// <summary>The password as the encoder is given it: "user:password" for an Icecast user other than "source".</summary>
    internal string Credentials => !Shoutcast && User.Trim() is { Length: > 0 } u && u != "source" ? $"{u}:{Password}" : Password;

    /// <summary>Only the address: never the password (a record would print it in a log).</summary>
    public override string ToString() => Address;
}

/// <summary>What Sintoniza Online streams, and how it keeps the stream up.</summary>
/// <param name="Input">The input to stream (empty: the default input); used when <paramref name="SourceUrl"/> is null.</param>
/// <param name="SourceUrl">A stream to relay instead of an input.</param>
/// <param name="Backup">A second server, used when the main one fails <paramref name="BackupAfter"/> times in a row.</param>
/// <param name="RetrySeconds">Between attempts to connect (after a lost connection the first attempt is at once).</param>
/// <param name="Fallback">Sent instead (looped) when the source has no sound for <paramref name="FallbackSeconds"/> (null: silence).</param>
/// <param name="SilenceDb">Under this (peak, dBFS) the source counts as silent.</param>
/// <param name="MainCheckSeconds">On the backup server: the main one is tried again this often.</param>
public sealed record OnlineOptions(
    string Input,
    string? SourceUrl,
    OnlineServer Main,
    OnlineServer? Backup,
    StreamFormat Format = StreamFormat.Mp3,
    int Bitrate = 128,
    bool Mono = false,
    string Name = "",
    string Genre = "",
    string Url = "",
    string Description = "",
    bool Public = false,
    string Title = "",
    int RetrySeconds = 5,
    int BackupAfter = 2,
    string? Fallback = null,
    double FallbackSeconds = 30,
    double SilenceDb = -45,
    int MainCheckSeconds = 60);

public enum OnlineState { Off, Connecting, Online, Waiting }

/// <summary>
/// Sintoniza Online's engine: sends an input of this computer (or a stream it relays) to an Icecast or Shoutcast
/// server and keeps it there. The sound goes through a <see cref="StreamKeeper"/>, the stream's own clock: when the
/// source stops (an input unplugged, a relayed stream that dropped) or stays silent, the server keeps getting
/// silence, or the fallback file, and its listeners. A connection that fails or is lost is tried again every
/// <see cref="OnlineOptions.RetrySeconds"/>, also on the backup server, and from the backup it goes back to the main
/// server once that answers (with no gap: the main connection is made before the backup's is closed). An input that
/// stops sending sound is opened again. One start per instance: a new one to start again.
/// </summary>
public sealed class OnlineStreamer : IDisposable
{
    const int Rate = AudioGraph.SampleRate;
    /// <summary>No sound arriving from the source for this long: it is opened again.</summary>
    const double ReopenSeconds = 5;
    /// <summary>The server took nothing for this long: the connection is made again.</summary>
    const double StuckSeconds = 20;

    readonly OnlineOptions _o;
    readonly RecordProcedure _recordProc;
    readonly object _lock = new();
    readonly float _silence;
    StreamKeeper? _keeper;
    Thread? _link, _source;
    volatile bool _running;
    int _record, _inputDevice = -1, _monitor, _encoder;
    long _lastData, _lastReal, _lastSound, _sentBefore; // _lastData: the reopen clock (reset at each reopen); _lastReal: sound really arrived
    float _peakLeft, _peakRight;
    float[] _scratch = new float[Rate];
    bool _holding;

    /// <summary>What happened (connected, lost, trying again, the input reopened…), for the log. From its own threads.</summary>
    public event Action<string>? Log;

    public OnlineStreamer(OnlineOptions options)
    {
        _o = options with
        {
            RetrySeconds = Math.Clamp(options.RetrySeconds, 1, 3600), BackupAfter = Math.Max(1, options.BackupAfter),
            MainCheckSeconds = Math.Max(1, options.MainCheckSeconds),
            FallbackSeconds = Math.Max(2, options.FallbackSeconds),
        };
        _recordProc = (_, buffer, length, _) =>
        {
            Take(buffer, length);
            return _running;
        };
        _silence = (float)Math.Pow(10, Math.Clamp(options.SilenceDb, -90, 0) / 20);
    }

    /// <summary>The inputs that can be streamed, and the outputs it can be heard on.</summary>
    public static IReadOnlyList<string> Inputs() => Microphone.DeviceNames();
    public static IReadOnlyList<string> Outputs() => AudioGraph.DeviceNames();

    public bool IsRunning => _running;

    // ───────────────────────── What the screen shows ─────────────────────────

    OnlineState _state;
    bool _onBackup;
    DateTime? _onlineSince, _nextAttempt;
    string? _error;
    int _connections;

    public OnlineState State { get { lock (_lock) return _state; } }
    /// <summary>On the backup server now.</summary>
    public bool OnBackup { get { lock (_lock) return _onBackup; } }
    /// <summary>When this connection was made (UTC); null: not connected.</summary>
    public DateTime? OnlineSince { get { lock (_lock) return _onlineSince; } }
    /// <summary>When it tries to connect again (UTC), while waiting.</summary>
    public DateTime? NextAttempt { get { lock (_lock) return _nextAttempt; } }
    /// <summary>Why the last attempt failed or the connection was lost (null once connected).</summary>
    public string? Error { get { lock (_lock) return _error; } }
    /// <summary>Connections made since it started (the first one and every reconnection).</summary>
    public int Connections { get { lock (_lock) return _connections; } }
    /// <summary>The server it is connected or connecting to.</summary>
    public OnlineServer Server => OnBackup && _o.Backup != null ? _o.Backup : _o.Main;

    /// <summary>Bytes sent to the servers since it started.</summary>
    public long BytesSent
    {
        get
        {
            int enc = Volatile.Read(ref _encoder);
            long now = enc != 0 ? Math.Max(0, BassEnc.EncodeGetCount(enc, EncodeCount.Cast)) : 0;
            return Interlocked.Read(ref _sentBefore) + now;
        }
    }

    /// <summary>Where the sound comes from: "Input: Line In (USB Audio)" / "Relay: http://…".</summary>
    public string Source => _o.SourceUrl is { } url ? L.F("Relay: {0}", WithoutPassword(url)) : L.F("Input: {0}", _o.Input.Length > 0 ? _o.Input : L.T("the default input"));

    /// <summary>An address as shown and logged: a user and password in it (http://user:pass@host/…) left out.</summary>
    public static string WithoutPassword(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0 ? url.Replace(uri.UserInfo + "@", "", StringComparison.Ordinal) : url;

    /// <summary>How it is sent: "MP3 128 kbps, stereo".</summary>
    public string Format => $"{(_o.Format == StreamFormat.Mp3 ? "MP3" : "Ogg Vorbis")} {_o.Bitrate} kbps, {(_o.Mono ? L.T("mono") : L.T("stereo"))}";

    /// <summary>Seconds since sound last arrived from the source (an input unplugged or hung, a relayed stream that dropped), across reopenings.</summary>
    public double SecondsWithoutData => _running ? (Environment.TickCount64 - Interlocked.Read(ref _lastReal)) / 1000.0 : 0;

    /// <summary>Since the source was last opened, or last sent sound: for opening it again.</summary>
    double SinceData => (Environment.TickCount64 - Interlocked.Read(ref _lastData)) / 1000.0;

    /// <summary>Seconds the source has been under the silence level (or sent nothing).</summary>
    public double SilentSeconds => _running ? (Environment.TickCount64 - Interlocked.Read(ref _lastSound)) / 1000.0 : 0;

    /// <summary>Whether the fallback file is what the server gets now.</summary>
    public bool OnFallback => _keeper?.OnFallback == true;

    /// <summary>The highest level (0–1, left and right) since this was last asked.</summary>
    public (float Left, float Right) TakePeaks()
    {
        lock (_lock)
        {
            var peaks = (_peakLeft, _peakRight);
            _peakLeft = _peakRight = 0;
            return peaks;
        }
    }

    // ───────────────────────── Start ─────────────────────────

    /// <summary>Starts the source and the connection (the first attempt at once). Throws only if the audio library can't start.</summary>
    public void Start()
    {
        if (_running || _keeper != null) return;
        PlayoutEngine.LoadPlugins();
        if (!Bass.Init(0) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException(L.F("The audio library can't start ({0})", Bass.LastError));
        _keeper = new StreamKeeper(0, m => Log?.Invoke(m), () => false); // stalls said here, in Sintoniza Online's words
        _keeper.SetFallback(_o.Fallback, TimeSpan.FromSeconds(_o.FallbackSeconds));
        _running = true;
        long now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastData, now);
        Interlocked.Exchange(ref _lastReal, now);
        Interlocked.Exchange(ref _lastSound, now);
        _source = new Thread(_o.SourceUrl != null ? Relay : WatchInput) { IsBackground = true, Name = "Sintoniza Online source" };
        _source.Start();
        _link = new Thread(Link) { IsBackground = true, Name = "Sintoniza Online link", Priority = ThreadPriority.AboveNormal };
        _link.Start();
    }

    // ───────────────────────── The source ─────────────────────────

    /// <summary>A block of sound from the source (stereo float): the level, silence, the monitor, and on to the stream.</summary>
    void Take(IntPtr buffer, int length)
    {
        try
        {
            if (length <= 0 || !_running || _keeper is not { } keeper) return;
            long now = Environment.TickCount64;
            Interlocked.Exchange(ref _lastData, now);
            Interlocked.Exchange(ref _lastReal, now);
            int samples = length / sizeof(float);
            if (_scratch.Length < samples) _scratch = new float[samples];
            Marshal.Copy(buffer, _scratch, 0, samples);
            float left = 0, right = 0;
            for (int i = 0; i + 1 < samples; i += 2)
            {
                left = Math.Max(left, Math.Abs(_scratch[i]));
                right = Math.Max(right, Math.Abs(_scratch[i + 1]));
            }
            lock (_lock)
            {
                _peakLeft = Math.Max(_peakLeft, left);
                _peakRight = Math.Max(_peakRight, right);
            }
            if (Math.Max(left, right) >= _silence) Interlocked.Exchange(ref _lastSound, now);
            if (_monitor is var monitor and not 0) Bass.StreamPutData(monitor, buffer, length);
            // A silent source, with a fallback file: held back, so the stream runs dry and the fallback comes in
            // (at once: the source has been silent long enough). Sound again: the source again.
            bool hold = _o.Fallback != null && (now - Interlocked.Read(ref _lastSound)) / 1000.0 >= Math.Max(0, _o.FallbackSeconds - StreamKeeper.StallSeconds);
            if (hold != _holding)
            {
                _holding = hold;
                keeper.FallbackAfter = hold ? TimeSpan.Zero : TimeSpan.FromSeconds(_o.FallbackSeconds);
                Log?.Invoke(hold ? L.F("Silence for {0:0} s: the fallback file goes out instead", _o.FallbackSeconds) : L.T("Sound again: the source goes out again"));
            }
            if (!hold) keeper.Feed(buffer, length);
        }
        catch (Exception) { /* never thrown into the sound library's thread */ }
    }

    /// <summary>The input: opened, and opened again when it stops sending sound (unplugged and plugged back, a driver that hung).</summary>
    void WatchInput()
    {
        string? said = null;
        while (_running)
        {
            if (_record == 0)
            {
                string? problem = OpenInput();
                if (problem == null)
                {
                    said = null;
                    Log?.Invoke(L.F("Streaming from {0}", Source));
                }
                else if (problem != said)
                {
                    said = problem;
                    Log?.Invoke(L.F("{0}; trying again every 5 s (the stream goes on meanwhile)", problem));
                }
                if (problem != null) { Sleep(5000); continue; }
            }
            Sleep(500);
            if (_running && SinceData > ReopenSeconds)
            {
                Log?.Invoke(L.T("The input stopped sending sound; opening it again"));
                CloseInput();
                Interlocked.Exchange(ref _lastData, Environment.TickCount64); // the next try after another few seconds
            }
        }
        CloseInput();
    }

    /// <returns>null when open; else what went wrong.</returns>
    string? OpenInput()
    {
        int device = FindInput(_o.Input);
        if (device < 0) return _o.Input.Length > 0 ? L.F("The input {0} isn't there", _o.Input) : L.T("No input to stream from was found");
        if (!Bass.RecordInit(device) && Bass.LastError != Errors.Already) return L.F("The input can't be opened ({0})", Bass.LastError);
        Bass.CurrentRecordingDevice = device;
        int channel = Bass.RecordStart(Rate, 2, BassFlags.Float, 20, _recordProc, IntPtr.Zero);
        if (channel == 0)
        {
            var error = Bass.LastError;
            Bass.RecordFree();
            return L.F("The input can't be opened ({0})", error);
        }
        _inputDevice = device;
        _record = channel;
        Interlocked.Exchange(ref _lastData, Environment.TickCount64);
        MonitorAttach();
        return null;
    }

    /// <summary>
    /// The input by its name; the default input when no name is given. A named input that isn't there is waited for,
    /// never another one instead: whatever microphone is the default would go out on the internet.
    /// </summary>
    static int FindInput(string name)
    {
        int first = -1, systemDefault = -1;
        for (int i = 0; Bass.RecordGetDeviceInfo(i, out var info); i++)
        {
            if (!info.IsEnabled) continue;
            if (name.Length > 0)
            {
                if (info.Name == name) return i;
                continue;
            }
            if (first < 0) first = i;
            if (info.IsDefault && systemDefault < 0) systemDefault = i;
        }
        return name.Length > 0 ? -1 : systemDefault >= 0 ? systemDefault : first;
    }

    void CloseInput()
    {
        if (_record != 0) Bass.ChannelStop(_record);
        _record = 0;
        if (_inputDevice < 0) return;
        Bass.CurrentRecordingDevice = _inputDevice;
        Bass.RecordFree();
        _inputDevice = -1;
    }

    /// <summary>A stream relayed: decoded here at the stream's rate, opened again when it drops or stops sending.</summary>
    void Relay()
    {
        var buffer = new float[Rate / 5 * 2]; // 200 ms
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        string? said = null;
        try
        {
            while (_running)
            {
                int stream = Bass.CreateStream(_o.SourceUrl!, 0, BassFlags.Decode | BassFlags.Float | BassFlags.StreamDownloadBlocks, null, IntPtr.Zero);
                int mixer = 0;
                if (stream != 0)
                {
                    // Through a mixer: any rate and any number of channels comes out as the stream's.
                    mixer = BassMix.CreateMixerStream(Rate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerEnd);
                    if (mixer == 0 || !BassMix.MixerAddChannel(mixer, stream, BassFlags.MixerChanDownMix))
                    {
                        if (mixer != 0) Bass.StreamFree(mixer);
                        Bass.StreamFree(stream);
                        stream = mixer = 0;
                    }
                }
                if (stream == 0)
                {
                    string problem = L.F("The stream to relay can't be opened ({0})", Bass.LastError);
                    if (problem != said) Log?.Invoke(L.F("{0}; trying again every 5 s (the stream goes on meanwhile)", problem));
                    said = problem;
                    Sleep(5000);
                    continue;
                }
                said = null;
                Log?.Invoke(L.F("Streaming from {0}", Source));
                Interlocked.Exchange(ref _lastData, Environment.TickCount64);
                long lastGot = Environment.TickCount64;
                while (_running)
                {
                    int n = Bass.ChannelGetData(mixer, handle.AddrOfPinnedObject(), buffer.Length * sizeof(float) | (int)DataFlags.Float);
                    if (n < 0 || Bass.ChannelIsActive(stream) == PlaybackState.Stopped) break; // ended or failed
                    if (n > 0)
                    {
                        lastGot = Environment.TickCount64;
                        Take(handle.AddrOfPinnedObject(), n);
                    }
                    else if (Environment.TickCount64 - lastGot > ReopenSeconds * 2000) break; // connected, but nothing comes
                    else Thread.Sleep(20);
                }
                Bass.StreamFree(mixer);
                Bass.StreamFree(stream);
                if (!_running) break;
                Log?.Invoke(L.T("The stream to relay stopped; opening it again"));
                Sleep(2000);
            }
        }
        finally { handle.Free(); }
    }

    void Sleep(int ms)
    {
        for (int i = 0; i < ms / 100 && _running; i++) Thread.Sleep(100);
    }

    // ───────────────────────── The connection ─────────────────────────

    /// <summary>Connects, watches the connection, and connects again: all on this thread (connecting blocks).</summary>
    void Link()
    {
        int failures = 0;
        string? lastSaid = null;
        var lastSaidAt = DateTime.MinValue;
        var next = DateTime.UtcNow;
        var nextMain = DateTime.MaxValue;
        long lastCount = -1, lastMoved = Environment.TickCount64;
        while (_running)
        {
            int enc = Volatile.Read(ref _encoder);
            var now = DateTime.UtcNow;
            if (enc == 0)
            {
                if (now < next) { Thread.Sleep(200); continue; }
                SetState(OnlineState.Connecting, null, null);
                string? error = Connect(_o.Main, out enc);
                bool backup = false;
                if (enc == 0 && _o.Backup is { IsSet: true } b && failures + 1 >= _o.BackupAfter)
                {
                    if (failures == 0 || error != lastSaid) Log?.Invoke(L.F("Could not connect to {0} ({1}); trying the backup server", _o.Main.Address, error));
                    string? backupError = Connect(b, out enc);
                    if (enc == 0) error = L.F("{0}; the backup server: {1}", error, backupError);
                    backup = enc != 0;
                }
                if (!_running) { Hang(enc); break; }
                if (enc == 0)
                {
                    failures++;
                    next = DateTime.UtcNow.AddSeconds(_o.RetrySeconds);
                    SetState(OnlineState.Waiting, error, next);
                    // Said when the reason changes, else every 10 minutes: an outage at a try a second is not thousands of lines.
                    if (error != lastSaid || DateTime.UtcNow - lastSaidAt > TimeSpan.FromMinutes(10))
                    {
                        Log?.Invoke(L.F("Could not connect ({0}); trying again in {1} s", error, _o.RetrySeconds) + (failures > 1 ? " " + L.F("(attempt {0})", failures) : ""));
                        lastSaid = error;
                        lastSaidAt = DateTime.UtcNow;
                    }
                    continue;
                }
                failures = 0;
                lastSaid = null;
                Volatile.Write(ref _encoder, enc);
                lastCount = -1;
                lastMoved = Environment.TickCount64;
                nextMain = backup ? DateTime.UtcNow.AddSeconds(_o.MainCheckSeconds) : DateTime.MaxValue;
                lock (_lock)
                {
                    _state = OnlineState.Online;
                    _onBackup = backup;
                    _onlineSince = DateTime.UtcNow;
                    _nextAttempt = null;
                    _error = null;
                    _connections++;
                }
                Log?.Invoke(L.F(backup ? "Online on the backup server {0} ({1})" : "Online: {0} ({1})", (backup ? _o.Backup! : _o.Main).Address, Format));
                continue;
            }

            Thread.Sleep(200);
            // Lost: the server closed it, or stopped taking it for a while (the encoder gives up).
            string? lost = null;
            if (BassEnc.EncodeIsActive(enc) == PlaybackState.Stopped) lost = L.T("Connection to the server was lost");
            else
            {
                long count = BassEnc.EncodeGetCount(enc, EncodeCount.Cast);
                if (count != lastCount) { lastCount = count; lastMoved = Environment.TickCount64; }
                else if (Environment.TickCount64 - lastMoved > StuckSeconds * 1000) lost = L.F("The server took nothing for {0:0} s", StuckSeconds);
            }
            if (lost != null)
            {
                Hang(Interlocked.Exchange(ref _encoder, 0));
                next = DateTime.UtcNow.AddSeconds(1); // at once first; then every RetrySeconds
                SetState(OnlineState.Waiting, lost, next);
                Log?.Invoke(L.F("{0}; connecting again", lost));
                continue;
            }
            // On the backup: back to the main server once it answers (connected there before the backup is left).
            if (OnBackup && DateTime.UtcNow >= nextMain)
            {
                nextMain = DateTime.UtcNow.AddSeconds(_o.MainCheckSeconds);
                Connect(_o.Main, out int main);
                if (main == 0) continue;
                if (!_running) { Hang(main); break; }
                Hang(Interlocked.Exchange(ref _encoder, main));
                lastCount = -1;
                lastMoved = Environment.TickCount64;
                nextMain = DateTime.MaxValue;
                lock (_lock)
                {
                    _onBackup = false;
                    _onlineSince = DateTime.UtcNow;
                    _connections++;
                }
                Log?.Invoke(L.F("Back on the main server {0}", _o.Main.Address));
            }
        }
        Hang(Interlocked.Exchange(ref _encoder, 0));
        Interlocked.Exchange(ref _keeper, null)?.Dispose(); // when Stop didn't wait for a connection being made
    }

    void SetState(OnlineState state, string? error, DateTime? next)
    {
        lock (_lock)
        {
            _state = state;
            _error = error;
            _nextAttempt = next;
            _onlineSince = null;
            _onBackup = false; // the next attempt starts with the main server
        }
    }

    /// <summary>An encoder on the stream's clock, connected to <paramref name="server"/>.</summary>
    /// <returns>What went wrong (null: connected, <paramref name="encoder"/> sending).</returns>
    string? Connect(OnlineServer server, out int encoder)
    {
        encoder = 0;
        if (!server.IsSet) return L.T("no server set");
        if (_keeper is not { } keeper) return L.T("not started");
        var flags = _o.Mono ? EncodeFlags.Mono : EncodeFlags.Default;
        int enc = _o.Format == StreamFormat.Mp3
            ? BassEnc_Mp3.Start(keeper.Handle, $"-b {_o.Bitrate}{(_o.Mono ? " -m m" : "")}", flags, null, IntPtr.Zero)
            : BassEnc_Ogg.Start(keeper.Handle, $"-b {_o.Bitrate}", flags, null, IntPtr.Zero);
        if (enc == 0) return L.F("the encoder can't start ({0})", Bass.LastError);
        string mime = _o.Format == StreamFormat.Mp3 ? BassEnc.MimeMp3 : BassEnc.MimeOgg;
        string? Text(string s) => s.Trim().Length > 0 ? s.Trim() : null;
        if (!BassEnc.CastInit(enc, server.Address, server.Credentials, mime, Text(_o.Name), Text(_o.Url), Text(_o.Genre), Text(_o.Description), null, _o.Bitrate, _o.Public))
        {
            var error = Bass.LastError;
            BassEnc.EncodeStop(enc);
            return error switch
            {
                Errors.CastDenied => server.Shoutcast
                    ? L.T("the server refused it: check the password")
                    : L.T("the server refused it: check the password and the mount, or another source is on that mount"),
                Errors.FileOpen => L.T("no answer from the server: check its address and port, and the internet connection"),
                Errors.Timeout => L.T("the server took too long to answer"),
                _ => L.F("the server can't be reached ({0})", error),
            };
        }
        if (Text(_o.Title) is { } title) BassEnc.CastSetTitle(enc, title, null);
        encoder = enc;
        return null;
    }

    /// <summary>Closes a connection, its bytes counted.</summary>
    void Hang(int enc)
    {
        if (enc == 0) return;
        Interlocked.Add(ref _sentBefore, Math.Max(0, BassEnc.EncodeGetCount(enc, EncodeCount.Cast)));
        BassEnc.EncodeStop(enc);
    }

    // ───────────────────────── Monitor ─────────────────────────

    string? _monitorOutput;

    /// <summary>Hear the source on an output (null: off).</summary>
    public void SetMonitor(string? output)
    {
        _monitorOutput = output;
        if (_running) MonitorAttach();
    }

    readonly object _monitorLock = new();

    void MonitorAttach()
    {
        lock (_monitorLock) MonitorAttachLocked();
    }

    void MonitorAttachLocked()
    {
        int old = Interlocked.Exchange(ref _monitor, 0);
        if (old != 0) Bass.StreamFree(old);
        if (_monitorOutput is not { } output) return;
        int device = 1;
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled && (info.Name == output || (output.Length == 0 && info.IsDefault))) { device = i; break; }
        if (!Bass.Init(device) && Bass.LastError != Errors.Already) { Log?.Invoke(L.F("Can't open {0} ({1})", output, Bass.LastError)); return; }
        Bass.CurrentDevice = device;
        int stream = Bass.CreateStream(Rate, 2, BassFlags.Float, StreamProcedureType.Push);
        if (stream == 0) return;
        Bass.ChannelPlay(stream);
        _monitor = stream;
    }

    // ───────────────────────── Stop ─────────────────────────

    /// <summary>Hangs up and stops the source. A connection being made is closed as soon as it is made.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _link?.Join(1000);
        _source?.Join(3000);
        Hang(Interlocked.Exchange(ref _encoder, 0));
        int monitor = Interlocked.Exchange(ref _monitor, 0);
        if (monitor != 0) Bass.StreamFree(monitor);
        // Still connecting (that blocks for a few seconds): the link frees the stream's clock when it ends.
        if (_link?.IsAlive != true) Interlocked.Exchange(ref _keeper, null)?.Dispose();
        lock (_lock)
        {
            _state = OnlineState.Off;
            _onlineSince = _nextAttempt = null;
        }
        Log?.Invoke(L.T("Streaming stopped"));
    }

    public void Dispose() => Stop();
}
