using System.Diagnostics;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>
/// The stream's own clock. What goes on air (Program, pulled by the music card) is copied into a buffer as the card
/// plays it, and a thread of its own sends it on to the encoder in real time. If the card stops being fed (a share
/// that doesn't answer, a USB card pulled out, a driver that hangs), the stream doesn't stop with it: it goes on with
/// silence, so the server keeps its listeners, and after <see cref="FallbackAfter"/> with the fallback file (looped,
/// read into memory beforehand so it never needs the share). Once the card plays again, the stream follows it again.
/// The send rate is nudged (at most 1%) to keep the buffer half a second full, so the card's clock and this
/// computer's never drift apart, with no resampling; after the server held up a send, what piled up goes at once.
/// </summary>
sealed class StreamKeeper : IDisposable
{
    const int Rate = AudioGraph.SampleRate;
    const int Frames = Rate * 4;          // the buffer: 4 s (a burst past it drops the oldest)
    const int Target = Rate / 2;          // kept this full: the stream is half a second behind the card
    const int TickMs = 20;
    /// <summary>No sound from the card for this long: the card has stopped (said in the log and on the lamp).</summary>
    public const double StallSeconds = 1.5;
    /// <summary>A larger fallback file isn't kept in memory (it is meant to be a short loop).</summary>
    const long MaxFallbackBytes = 80L * 1024 * 1024;

    readonly int _program;
    readonly float[] _ring = new float[Frames * 2];
    int _read, _count;                    // in frames (guarded by _lock)
    readonly object _lock = new();
    readonly DSPProcedure _dsp;
    readonly StreamProcedure _proc;
    readonly int _dspHandle;
    long _lastFed = Environment.TickCount64; // when the card last sent sound (Volatile)
    bool _starved = true;                 // waiting for the buffer to fill (at the start, and after a stall)
    float[] _scratch = new float[Rate];   // the card's block (card thread, under _lock)
    float[] _block = new float[Rate];     // what the encoder gets (keeper thread)
    float[] _rest = new float[Rate];      // from the fallback file (keeper thread)

    // The fallback file: a decoding mixer over it, and the file's own stream to take back to its start. Used and
    // freed under _fallbackLock (never _lock: the card's thread never waits for a file being decoded).
    readonly object _fallbackLock = new();
    int _fallback, _fallbackSource;
    (string Path, DateTime Modified, long Size)? _fallbackFile; // what is read now (not read again while unchanged)
    string? _problemSaid;                 // the last "can't be used" said in the log (not said again at every reconnect)
    bool _onFallback;
    int _loading;                         // a fallback file being read (Interlocked)

    volatile bool _stop;
    readonly Thread _thread;
    readonly Action<string> _log;
    readonly Func<bool> _streaming;
    long _stalledSince;                   // the card's last sound before the stall said in the log (0: none)
    bool _fallbackSaid;

    /// <summary>The decoding channel the encoder is started on.</summary>
    public int Handle { get; }

    /// <summary>How long after the card stopped the fallback file is sent instead of silence.</summary>
    public TimeSpan FallbackAfter { get; private set; } = TimeSpan.FromSeconds(10);

    /// <param name="streaming">Whether a stream is on air now: only then is a stall said in the log.</param>
    public StreamKeeper(int program, Action<string> log, Func<bool> streaming)
    {
        _program = program;
        _log = log;
        _streaming = streaming;
        _dsp = Copy;
        _proc = Fill;
        Handle = Bass.CreateStream(Rate, 2, BassFlags.Decode | BassFlags.Float, _proc, IntPtr.Zero);
        if (Handle == 0) throw new InvalidOperationException(L.F("Stream: {0}", Bass.LastError));
        // Called with each block the card takes from Program (also when nothing is streamed: the buffer stays current).
        _dspHandle = Bass.ChannelSetDSP(program, _dsp, IntPtr.Zero, -1000);
        if (_dspHandle == 0)
        {
            var error = Bass.LastError;
            Bass.StreamFree(Handle);
            throw new InvalidOperationException(L.F("Stream: {0}", error));
        }
        _thread = new Thread(Run) { IsBackground = true, Name = "Stream clock", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Seconds since the card last sent sound, while the stream goes on without it (0: it is following the card).</summary>
    public double Stalled
    {
        get
        {
            double since = (Environment.TickCount64 - Volatile.Read(ref _lastFed)) / 1000.0;
            return since >= StallSeconds ? since : 0;
        }
    }

    /// <summary>Whether the fallback file is what the stream sends now.</summary>
    public bool OnFallback => Volatile.Read(ref _onFallback);

    /// <summary>
    /// The file sent while the card is stopped (empty: silence), and <paramref name="after"/> how long after the card
    /// stopped. The file is read into memory in the background (never holding up the stream's start); the same file
    /// unchanged isn't read again, and one that can't be read leaves the one read before (said in the log, once).
    /// </summary>
    public void SetFallback(string? file, TimeSpan after)
    {
        FallbackAfter = after < TimeSpan.Zero ? TimeSpan.Zero : after;
        string? wanted = string.IsNullOrWhiteSpace(file) ? null : file;
        lock (_fallbackLock) _wanted = wanted;
        if (wanted == null) Swap(0, 0, null);
        else StartLoad();
    }

    string? _wanted;                      // the fallback file chosen last (guarded by _fallbackLock)

    string? Wanted
    {
        get { lock (_fallbackLock) return _wanted; }
    }

    /// <summary>Reads the chosen file, one read at a time; one chosen while a read ran is read after it.</summary>
    void StartLoad()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1) return; // the read running looks at what is wanted when it ends
        Task.Run(() =>
        {
            string? done = null;
            try
            {
                while (!_stop && Wanted is { } file && file != done)
                {
                    Load(file);
                    done = file;
                }
            }
            finally { Volatile.Write(ref _loading, 0); }
            if (!_stop && Wanted is { } later && later != done) StartLoad(); // chosen just as it ended
        });
    }

    void Load(string file)
    {
        string local = SharedPaths.ToLocal(file);
        (string, DateTime, long)? stamp = null;
        try
        {
            var info = new FileInfo(local);
            if (info.Exists) stamp = (file, info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception) { /* not reachable now */ }
        lock (_fallbackLock)
            if (stamp != null && _fallbackFile == stamp) return; // read already

        string? problem = null;
        int mixer = 0, source = 0;
        try
        {
            if (stamp is not { } s) problem = L.T("not found");
            else if (s.Item3 > MaxFallbackBytes) problem = L.T("over 80 MB");
            else
            {
                var bytes = File.ReadAllBytes(local);
                source = FileCache.Open(bytes, BassFlags.Decode | BassFlags.Float | BassFlags.Loop);
                if (source == 0) problem = Bass.LastError.ToString();
                else
                {
                    // Through a mixer: any rate and any number of channels comes out as the stream's.
                    mixer = BassMix.CreateMixerStream(Rate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop);
                    if (mixer == 0 || !BassMix.MixerAddChannel(mixer, source, BassFlags.MixerChanDownMix))
                    {
                        problem = Bass.LastError.ToString();
                        if (mixer != 0) Bass.StreamFree(mixer);
                        Bass.StreamFree(source);
                        mixer = source = 0;
                    }
                }
            }
        }
        catch (Exception ex) { problem = ex.Message; }

        if (problem != null)
        {
            // The one read before (if any) stays: a share away now is when it is wanted. Tried again at the next start.
            string said = file + "|" + problem;
            if (Interlocked.Exchange(ref _problemSaid, said) != said)
                _log(L.F("Stream: the fallback file can't be used ({0}); silence instead", problem));
            return;
        }
        _problemSaid = null;
        // Only if still wanted: another file (or none) chosen meanwhile, or closed, and what was read goes.
        Swap(mixer, source, stamp, only: file);
    }

    /// <param name="only">Only if this is still the file wanted (else <paramref name="mixer"/> and <paramref name="source"/> are freed).</param>
    void Swap(int mixer, int source, (string, DateTime, long)? file, string? only = null)
    {
        int oldMixer, oldSource;
        lock (_fallbackLock)
        {
            if (only != null && (_stop || _wanted != only)) (oldMixer, oldSource) = (mixer, source);
            else
            {
                (oldMixer, oldSource) = (_fallback, _fallbackSource);
                (_fallback, _fallbackSource, _fallbackFile) = (mixer, source, file);
            }
        }
        if (oldMixer != 0) Bass.StreamFree(oldMixer);
        if (oldSource != 0) Bass.StreamFree(oldSource);
    }

    /// <summary>The card took a block from Program: kept for the stream.</summary>
    void Copy(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        try
        {
            int frames = length / (2 * sizeof(float));
            if (frames <= 0) return;
            lock (_lock)
            {
                if (_scratch.Length < frames * 2) _scratch = new float[frames * 2];
                Marshal.Copy(buffer, _scratch, 0, frames * 2);
                int skip = Math.Max(0, frames - Frames); // more than the buffer holds at once: only its end
                for (int i = skip; i < frames; i++)
                {
                    if (_count == Frames) { _read = (_read + 1) % Frames; _count--; } // full: the oldest goes
                    int at = (_read + _count) % Frames * 2;
                    _ring[at] = _scratch[i * 2];
                    _ring[at + 1] = _scratch[i * 2 + 1];
                    _count++;
                }
            }
            Volatile.Write(ref _lastFed, Environment.TickCount64);
        }
        catch (Exception) { /* never thrown into the sound library's thread */ }
    }

    /// <summary>What the encoder gets: the buffer, or once it ran dry until it is half a second full again, silence or the fallback.</summary>
    int Fill(int handle, IntPtr buffer, int length, IntPtr user)
    {
        try
        {
            int frames = length / (2 * sizeof(float));
            if (_block.Length < frames * 2) _block = new float[frames * 2];
            int take;
            lock (_lock)
            {
                if (_starved && _count >= Target) _starved = false;
                take = _starved ? 0 : Math.Min(frames, _count);
                for (int i = 0; i < take; i++)
                {
                    int at = (_read + i) % Frames * 2;
                    _block[i * 2] = _ring[at];
                    _block[i * 2 + 1] = _ring[at + 1];
                }
                _read = (_read + take) % Frames;
                _count -= take;
                if (take < frames) _starved = true;
            }
            Array.Clear(_block, take * 2, (frames - take) * 2);
            bool fallback = false;
            if (take < frames && Stalled > 0 && TimeSpan.FromSeconds(Stalled) >= FallbackAfter)
                lock (_fallbackLock)
                    if (_fallback != 0)
                    {
                        fallback = true;
                        // The rest of the block from the fallback file, from its start each time the card stops.
                        if (!_onFallback)
                        {
                            BassMix.ChannelSetPosition(_fallbackSource, 0);
                            Bass.ChannelSetPosition(_fallback, 0, PositionFlags.Bytes | PositionFlags.MixerReset);
                        }
                        int want = (frames - take) * 2;
                        if (_rest.Length < want) _rest = new float[want];
                        int got = Bass.ChannelGetData(_fallback, _rest, want * sizeof(float) | (int)DataFlags.Float);
                        if (got > 0) Array.Copy(_rest, 0, _block, take * 2, Math.Min(want, got / sizeof(float)));
                    }
            Volatile.Write(ref _onFallback, fallback);
            Marshal.Copy(_block, 0, buffer, frames * 2);
        }
        catch (Exception) { /* never thrown into the sound library's thread */ }
        return length;
    }

    /// <summary>Pulls the stream in real time (which encodes and sends it), the rate nudged to keep the buffer at its target.</summary>
    void Run()
    {
        var clock = Stopwatch.StartNew();
        double owed = 0;
        long last = 0;
        var sink = new float[Rate * 2 * 3];
        while (!_stop)
        {
            Thread.Sleep(TickMs);
            long now = clock.ElapsedMilliseconds;
            double seconds = Math.Min(0.2, (now - last) / 1000.0); // after the computer slept: no rush to catch up
            last = now;
            int level;
            bool starved;
            lock (_lock) { level = _count; starved = _starved; }
            double nudge = starved ? 0 : Math.Clamp((level - Target) / (double)Target * 0.01, -0.01, 0.01);
            owed += seconds * Rate * (1 + nudge);
            // A send the server held up (the encoder sends on this thread): what piled up in the buffer goes now.
            if (!starved && level > Target * 2) owed += level - Target;
            int frames = Math.Min((int)owed, sink.Length / 2);
            if (frames <= 0) continue;
            owed -= frames;
            try { Bass.ChannelGetData(Handle, sink, frames * 2 * sizeof(float) | (int)DataFlags.Float); }
            catch (Exception) { /* never the end of the stream's clock */ }
            Report();
        }
    }

    /// <summary>A stall said in the log once, when it starts and when the card plays again (only while streaming).</summary>
    void Report()
    {
        double stalled = Stalled;
        if (stalled > 0 && _stalledSince == 0)
        {
            if (!_streaming()) return;
            _stalledSince = Volatile.Read(ref _lastFed); // the last sound from the card
            _log(L.T("Stream: no sound from the sound card; the stream goes on without it"));
        }
        else if (stalled > 0 && OnFallback && !_fallbackSaid)
        {
            _fallbackSaid = true;
            _log(L.T("Stream: the fallback file is on the stream"));
        }
        else if (stalled == 0 && _stalledSince != 0)
        {
            _log(L.F("Stream: the sound card plays again (it stopped for {0:0} s)", (Environment.TickCount64 - _stalledSince) / 1000.0));
            _stalledSince = 0;
            _fallbackSaid = false;
        }
    }

    public void Dispose()
    {
        lock (_fallbackLock) _stop = true; // a read ending after this frees what it read
        _thread.Join(1000);
        Bass.ChannelRemoveDSP(_program, _dspHandle);
        Bass.StreamFree(Handle);
        Swap(0, 0, null);
    }
}
