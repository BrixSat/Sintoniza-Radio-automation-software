using System.Collections.Concurrent;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>
/// The on-air engine. Owns BASS, the main mixer, the playlist and the automation.
///
/// Threading: every command is posted to a single engine thread, so playlist state
/// is only ever touched there. BASS sync callbacks only enqueue work. The UI can hang
/// without stopping automation, and nothing in the UI ever calls BASS directly.
/// </summary>
public sealed class PlayoutEngine : IDisposable
{
    const double StopFadeSeconds = 0.4;
    const double CartStopFadeSeconds = 0.25;
    const double CueOutFadeSeconds = 0.05; // just enough to avoid a click when cutting at Cue Out
    const double StreamConnectLeadSeconds = 10; // connect to a stream this long before it is due, so connecting never means dead air
    const double SignalPrepSeconds = 3;    // schedule the time signal this long before it starts
    const double SignalFadeSeconds = 1;    // Stop mode: what is on air fades out over this, ending as the signal starts

    readonly BlockingCollection<Action> _commands = new();
    readonly List<(DateTime Due, Action Action)> _timers = new();
    readonly Thread _thread;

    // Strong references: BASS holds raw function pointers to these.
    readonly SyncProcedure _crossfadeSync;
    readonly SyncProcedure _endSync;
    readonly SyncProcedure _cueOutSync;

    readonly AudioGraph _graph;
    readonly Microphone _mic;
    readonly WaveformLoader _waveforms = new();
    readonly CueStore _cues;

    // Engine-thread state.
    readonly List<Track> _playlist = new();
    readonly HashSet<Guid> _played = new();
    int _cursorIndex = -1; // index of the last started item; survives removal of that item
    (Guid Id, int Handle)? _preload;
    int _connectAttempt;   // bumped by every start and stop, so a stream that connects too late is dropped
    Guid? _connecting;     // stream item that is due but still connecting; automation waits for it instead of starting it again
    (Guid Id, Task<int> Handle)? _streamConnection; // connection to a stream item, opened ahead of its start
    readonly List<int> _playingOut = new(); // decks past their Mix marker, finishing under the next item
    readonly ConcurrentDictionary<int, byte> _carts = new(); // carts fired and not stopped (some may have ended)
    TimeSignalSettings _timeSignal = new();
    double _signalHourAt;    // where the hour falls in the signal (seconds)
    DateTime? _signalHour;   // the next top of the hour that gets the signal (local time)
    bool _resumeAfterSignal; // Stop mode took the item off air: start the next one when the signal ends

    // Shared with polling readers; written only on the engine thread.
    volatile Deck? _current;
    volatile int _state = (int)PlayerState.Stopped;
    volatile int _encoder;
    volatile int _streamState = (int)StreamState.Off;
    volatile string? _streamError;
    int _streamAttempt;
    readonly object _streamLock = new(); // bumped by every start and stop, so a connection that completes after Stop is dropped

    volatile bool _autoAdvance = true;
    double _crossfadeSeconds = 5;

    /// <param name="MixFade">How the deck fades once the next item starts at the mix point; null = it plays on to Cue Out (Mix marker).</param>
    sealed record Deck(int Handle, Track Track, double Length, int MixSync, int CueOutSync, double? MixFade);

    public event Action<PlaylistSnapshot>? PlaylistChanged;
    public event Action<string>? Log;

    /// <param name="routing">Where the music, cart and preview buses go.</param>
    /// <param name="cues">Cue points per file; applied when files are added.</param>
    /// <param name="forceDevice">Send every bus to this BASS device regardless of routing (0 = "no sound", for testing).</param>
    public PlayoutEngine(AudioRouting routing, CueStore cues, int? forceDevice = null)
    {
        LoadPlugins();
        _cues = cues;

        _graph = new AudioGraph(forceDevice);
        _graph.Apply(routing);
        _mic = new Microphone(_graph, After, Emit);

        _crossfadeSync = (_, channel, _, _) => _commands.Add(() => OnCrossfadePoint(channel));
        _endSync = (_, channel, _, _) => _commands.Add(() => OnTrackEnd(channel));
        _cueOutSync = (_, channel, _, _) => _commands.Add(() => OnCueOut(channel));

        _thread = new Thread(Run) { Name = "PlayoutEngine", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public static IReadOnlyList<string> GetOutputDevices() => AudioGraph.DeviceNames();

    /// <summary>How many output channels a sound card has (2 for plain stereo cards).</summary>
    public int GetOutputChannelCount(string device) => _graph.ChannelCount(device);

    /// <summary>Re-route the buses. Applies immediately; outputs drop out for a moment.</summary>
    public void ApplyRouting(AudioRouting routing) => Post(() =>
    {
        _graph.Apply(routing);
        Emit("Audio outputs updated");
    });

    public static IReadOnlyList<string> GetInputDevices() => Microphone.DeviceNames();

    /// <summary>Sets up the web radio microphone; null turns it off (FM: the mic goes through the desk).</summary>
    public void ConfigureMic(MicSettings? settings) => Post(() => _mic.Configure(settings));

    /// <summary>Opens (on air, music ducked) or closes the microphone.</summary>
    public void SetMic(bool on) => Post(() => _mic.Set(on));

    /// <summary>True while the microphone is open.</summary>
    public bool MicLive => _mic.IsLive;

    /// <summary>Sets up the top of the hour time signal.</summary>
    public void ConfigureTimeSignal(TimeSignalSettings settings) => Post(() =>
    {
        _timeSignal = settings;
        _signalHour = null;
        if (settings.Mode == TimeSignalMode.Off) return;
        Bass.StreamFree(TimeSignal.Open(settings.File, Emit, out _signalHourAt, out _));
    });

    /// <summary>Plays the time signal (file, or empty for the pips) on the Preview output, to check it off air.</summary>
    public void PreviewTimeSignal(string file)
    {
        int h = TimeSignal.Open(file, Emit, out _, out _);
        if (h != 0 && !BassMix.MixerAddChannel(_graph.PreviewBus, h, BassFlags.AutoFree)) Bass.StreamFree(h);
    }

    /// <summary>Short 1 kHz tone on a card/channel pair, to find the right desk fader.</summary>
    public void PlayTestTone(OutputRoute route) => _graph.TestTone(route);

    /// <summary>True when the Preview output is the same card and pair as Music or Cartwall, so pre-listening would be heard on air.</summary>
    public bool PreviewIsOnAir => _graph.PreviewOnAir;

    /// <summary>Opens a file for pre-listening on the Preview output (paused at the start), or null if it cannot be read.</summary>
    public PreviewPlayer? OpenPreview(string path)
    {
        int h = OpenStream(path, prescan: true);
        if (h == 0) return null;
        if (!BassMix.MixerAddChannel(_graph.PreviewBus, h, BassFlags.MixerChanPause))
        {
            Bass.StreamFree(h);
            return null;
        }
        return new PreviewPlayer(_graph, h);
    }

    /// <summary>Saved cue points for a file (empty if none).</summary>
    public CuePoints GetCues(string path) => _cues.Get(path) ?? CuePoints.None;

    /// <summary>
    /// Saves cue points for a file and applies them to every playlist item of that file. If the file is on air,
    /// the new Mix and Cue Out take effect straight away (if they are still ahead).
    /// </summary>
    public void SetCues(string path, CuePoints cues)
    {
        _cues.Set(path, cues);
        Post(() =>
        {
            var comparer = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (int i = 0; i < _playlist.Count; i++)
                if (string.Equals(_playlist[i].Path, path, comparer))
                    _playlist[i] = _playlist[i] with { Cues = cues.IsEmpty ? null : cues };

            if (_current is { } deck && string.Equals(deck.Track.Path, path, comparer))
            {
                if (deck.MixSync != 0) BassMix.ChannelRemoveSync(deck.Handle, deck.MixSync);
                if (deck.CueOutSync != 0) BassMix.ChannelRemoveSync(deck.Handle, deck.CueOutSync);
                _current = Arm(deck.Handle, deck.Track with { Cues = cues.IsEmpty ? null : cues }, deck.Length);
            }
            PlaylistUpdated();
        });
    }

    // ───────────────────────── Public API (thread-safe, non-blocking) ─────────────────────────

    public bool AutoAdvance { get => _autoAdvance; set => _autoAdvance = value; }

    /// <summary>Crossfade length in seconds. Applies to tracks started after the change.</summary>
    public double CrossfadeSeconds
    {
        get => Volatile.Read(ref _crossfadeSeconds);
        set => Volatile.Write(ref _crossfadeSeconds, Math.Clamp(value, 0, 15));
    }

    public void AddFiles(IEnumerable<string> paths, int insertAt = -1)
    {
        var list = paths.ToList();
        Post(() =>
        {
            Insert(list.Select(SafeProbe).OfType<Track>().ToList(), insertAt);
        });
    }

    /// <summary>Adds another station's internet stream (http/https URL), played for <paramref name="seconds"/> or, with 0, until Stop or Next.</summary>
    public void AddStream(string url, string name, double seconds, int insertAt = -1) => Post(() =>
        Insert([new Track(Guid.NewGuid(), url.Trim(), "", string.IsNullOrWhiteSpace(name) ? url.Trim() : name.Trim(), Math.Max(0, seconds), TrackKind.Stream)], insertAt));

    void Insert(List<Track> tracks, int insertAt)
    {
        int at = insertAt < 0 || insertAt > _playlist.Count ? _playlist.Count : insertAt;
        _playlist.InsertRange(at, tracks);
        if (at <= _cursorIndex) _cursorIndex += tracks.Count;
        PlaylistUpdated();
    }

    public void Remove(Guid id) => Post(() =>
    {
        int i = _playlist.FindIndex(t => t.Id == id);
        if (i < 0) return;
        _playlist.RemoveAt(i);
        // Keep the cursor pointing "between" items so Next still means the following track.
        if (i <= _cursorIndex) _cursorIndex--;
        PlaylistUpdated();
    });

    /// <summary>
    /// Move items (kept in their current order) so they sit before the item now at
    /// <paramref name="insertAt"/>; an index past the end moves them to the bottom.
    /// </summary>
    public void Move(IReadOnlyCollection<Guid> ids, int insertAt) => Post(() =>
    {
        var moving = ids.ToHashSet();
        var block = _playlist.Where(t => moving.Contains(t.Id)).ToList();
        if (block.Count == 0) return;

        // Anchor on the first non-moving item at/after the drop point, so indices stay meaningful after removal.
        var anchor = _playlist.Skip(Math.Max(0, insertAt)).FirstOrDefault(t => !moving.Contains(t.Id));
        var cursorId = _cursorIndex >= 0 && _cursorIndex < _playlist.Count ? _playlist[_cursorIndex].Id : (Guid?)null;

        _playlist.RemoveAll(t => moving.Contains(t.Id));
        int at = anchor == null ? _playlist.Count : _playlist.IndexOf(anchor);
        _playlist.InsertRange(at, block);

        if (cursorId is { } cid) _cursorIndex = _playlist.FindIndex(x => x.Id == cid);
        PlaylistUpdated();
    });

    public void Clear() => Post(() =>
    {
        var current = _current?.Track;
        _playlist.Clear();
        _played.Clear();
        if (current != null) { _playlist.Add(current); _played.Add(current.Id); _cursorIndex = 0; }
        else _cursorIndex = -1;
        PlaylistUpdated();
    });

    /// <summary>Start playback. With an id, crossfade into that item now; otherwise resume or start the next item.</summary>
    public void Play(Guid? id = null) => Post(() =>
    {
        if (id is { } target)
        {
            var t = _playlist.FirstOrDefault(x => x.Id == target);
            if (t == null) return;
            FadePlayingOut(ManualFade);
            StartTrack(t, ManualFade);
            return;
        }
        if (_current != null && State == PlayerState.Paused) { SetPaused(false); return; }
        if (_current != null) return;
        // At the end of the list, Play starts again from the top.
        if ((NextTrack() ?? _playlist.FirstOrDefault()) is { } next) StartTrack(next, ManualFade);
    });

    /// <summary>Crossfade into the next playlist item immediately.</summary>
    public void Next() => Post(() =>
    {
        if (NextTrack() is not { } next) { StopInternal(); return; }
        FadePlayingOut(ManualFade);
        StartTrack(next, ManualFade);
    });

    public void Stop() => Post(StopInternal);

    public void TogglePause() => Post(() =>
    {
        if (_current == null) return;
        SetPaused(State != PlayerState.Paused);
    });

    /// <summary>Fire a cart (jingle/ID) over whatever is on air. Returns a handle for <see cref="StopCart"/>.</summary>
    public int FireCart(string path)
    {
        int h = OpenStream(path, prescan: false);
        if (h == 0) { Emit($"Cart failed: {Path.GetFileName(path)} ({Bass.LastError})"); return 0; }
        BassMix.MixerAddChannel(_graph.CartBus, h, BassFlags.AutoFree);
        _carts[h] = 0;
        return h;
    }

    public void StopCart(int handle)
    {
        if (handle == 0) return;
        _carts.TryRemove(handle, out _);
        Bass.ChannelSlideAttribute(handle, ChannelAttribute.Volume, 0, (int)(CartStopFadeSeconds * 1000));
        Post(() => After(CartStopFadeSeconds + 0.2, () => Bass.StreamFree(handle)));
    }

    /// <summary>Seconds left on a cart and how much of it has played (0–1), or null when it has finished.</summary>
    public (double Left, double Played)? CartProgress(int handle)
    {
        if (handle == 0 || Bass.ChannelIsActive(handle) == PlaybackState.Stopped) return null;
        long len = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
        if (len < 0) return null;
        // Until the mixer has rendered the first block there is no position yet.
        long pos = Math.Clamp(BassMix.ChannelGetPosition(handle, PositionFlags.Bytes, _graph.CartDelay), 0, len);
        return (Bass.ChannelBytes2Seconds(handle, len - pos), len > 0 ? (double)pos / len : 0);
    }

    /// <summary>Amplitude envelope for drawing; decoded off the on-air path and cached.</summary>
    public Task<Waveform?> LoadWaveformAsync(string path) =>
        IsUrl(path) ? Task.FromResult<Waveform?>(null) : _waveforms.LoadAsync(path);

    public static bool IsUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public PlayerStatus GetStatus()
    {
        var deck = _current;
        double pos = 0, len = 0;
        if (deck != null)
        {
            // Corrected for what is still queued on the card, so the display matches what is audible.
            long p = BassMix.ChannelGetPosition(deck.Handle, PositionFlags.Bytes, _graph.MusicDelay);
            if (p >= 0) pos = Bass.ChannelBytes2Seconds(deck.Handle, p);
            len = deck.Length;
        }

        var (left, right) = _graph.Levels();
        float mic = _mic.Level();
        left = Math.Min(1, left + mic);
        right = Math.Min(1, right + mic);

        return new PlayerStatus(State, deck?.Track, pos, len, left, right,
            (StreamState)_streamState, _streamError);
    }

    public PlayerState State => (PlayerState)_state;

    public void StartStreaming(StreamSettings s)
    {
        StopStreaming();
        int attempt = Interlocked.Increment(ref _streamAttempt);
        _streamState = (int)StreamState.Connecting;
        _streamError = null;

        // CastInit blocks while connecting, so keep it off the engine thread.
        Task.Run(() =>
        {
            int enc = s.Format == StreamFormat.Mp3
                ? BassEnc_Mp3.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero)
                : BassEnc_Ogg.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero);
            if (enc == 0) { FailStream($"Encoder start failed: {Bass.LastError}", attempt); return; }

            string mount = s.Mount.StartsWith('/') ? s.Mount : "/" + s.Mount;
            string server = s.Shoutcast ? $"{s.Host}:{s.Port}" : $"{s.Host}:{s.Port}{mount}";
            string mime = s.Format == StreamFormat.Mp3 ? BassEnc.MimeMp3 : BassEnc.MimeOgg;

            if (!BassEnc.CastInit(enc, server, s.Password, mime, s.StationName, s.Url, s.Genre, null, null, s.Bitrate, false))
            {
                var err = Bass.LastError;
                BassEnc.EncodeStop(enc);
                FailStream($"Could not connect to {server} ({err}); check host, port, mount and password", attempt);
                return;
            }

            lock (_streamLock)
            {
                if (attempt != _streamAttempt)
                {
                    // Stopped while connecting: hang up instead of going on air.
                    BassEnc.EncodeStop(enc);
                    return;
                }
                _encoder = enc;
                _streamState = (int)StreamState.OnAir;
            }
            Emit($"Streaming to {server} ({s.Format} {s.Bitrate} kbps)");
            if (_current?.Track is { } t) BassEnc.CastSetTitle(enc, t.Display, null);
        });
    }

    /// <summary>Stops streaming, cancels a connection in progress, or clears a stream error.</summary>
    public void StopStreaming()
    {
        int enc;
        bool connecting;
        lock (_streamLock)
        {
            Interlocked.Increment(ref _streamAttempt);
            connecting = _streamState == (int)StreamState.Connecting;
            enc = Interlocked.Exchange(ref _encoder, 0);
            _streamState = (int)StreamState.Off;
            _streamError = null;
        }
        if (enc != 0) { BassEnc.EncodeStop(enc); Emit("Streaming stopped"); }
        else if (connecting) Emit("Streaming cancelled");
    }

    public void Dispose()
    {
        StopStreaming();
        _commands.CompleteAdding();
        _thread.Join(2000);
        _mic.Free();
        _graph.Free();
    }

    // ───────────────────────── Engine thread ─────────────────────────

    void Post(Action a)
    {
        if (!_commands.IsAddingCompleted) _commands.Add(a);
    }

    void Run()
    {
        while (!_commands.IsCompleted)
        {
            if (_commands.TryTake(out var cmd, 50))
                Safe(cmd);
            RunTimers();
            WatchStream();
            Safe(PreconnectStream);
            Safe(CheckTimeSignal);
        }
    }

    void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Emit($"Engine error: {ex.Message}"); }
    }

    void After(double seconds, Action a) => _timers.Add((DateTime.UtcNow.AddSeconds(seconds), a));

    void RunTimers()
    {
        var now = DateTime.UtcNow;
        for (int i = _timers.Count - 1; i >= 0; i--)
        {
            if (_timers[i].Due > now) continue;
            var a = _timers[i].Action;
            _timers.RemoveAt(i);
            Safe(a);
        }
    }

    void WatchStream()
    {
        int enc = _encoder;
        if (enc != 0 && BassEnc.EncodeIsActive(enc) == PlaybackState.Stopped
            && Interlocked.CompareExchange(ref _encoder, 0, enc) == enc) // not if Stop got there first
        {
            FailStream("Connection to the streaming server was lost");
        }
    }

    /// <param name="attempt">The start this failure belongs to; ignored if streaming was stopped or restarted since.</param>
    void FailStream(string message, int? attempt = null)
    {
        lock (_streamLock)
        {
            if (attempt is { } a && a != _streamAttempt) return;
            _streamState = (int)StreamState.Failed;
            _streamError = message;
        }
        Emit(message);
    }

    Track? NextTrack()
    {
        int i = _cursorIndex + 1;
        return i >= 0 && i < _playlist.Count ? _playlist[i] : null;
    }

    /// <summary>Fade for the outgoing deck when the operator changes item by hand.</summary>
    double ManualFade => Math.Max(CrossfadeSeconds, StopFadeSeconds);

    void OnCrossfadePoint(int handle)
    {
        if (_current is not { } deck || deck.Handle != handle || !_autoAdvance) return;
        if (NextTrack() is { } next && next.Id != _connecting) StartTrack(next, deck.MixFade);
    }

    void OnTrackEnd(int handle)
    {
        _playingOut.Remove(handle);
        if (_current?.Handle != handle) return; // a faded-out deck finishing, nothing to do
        if (_current.Track.IsStream) Emit($"Stream ended: {_current.Track.Title}");
        if (_autoAdvance && NextTrack() is { } next)
        {
            if (next.Id == _connecting) { _current = null; PlaylistUpdated(); return; } // on air as soon as it connects
            StartTrack(next, 0);
            return;
        }
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    /// <summary>A deck reached its Cue Out: cut it, and if it is still the one on air, move on (AUTO) or stop.</summary>
    void OnCueOut(int handle)
    {
        if (_current?.Handle != handle)
        {
            // Playing out after its Mix point, or still fading: nothing past Cue Out may be heard.
            _playingOut.Remove(handle);
            FadeOutAndFree(handle, CueOutFadeSeconds);
            return;
        }
        if (_autoAdvance && NextTrack() is { } next)
        {
            if (next.Id == _connecting)
            {
                FadeOutAndFree(handle, CueOutFadeSeconds);
                _current = null;
                PlaylistUpdated();
                return;
            }
            StartTrack(next, CueOutFadeSeconds);
            return;
        }
        FadeOutAndFree(handle, CueOutFadeSeconds);
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    /// <param name="outgoingFade">How the deck now on air leaves: fade time in seconds (0 = cut), or null to let it play on to its Cue Out.</param>
    void StartTrack(Track track, double? outgoingFade)
    {
        _connectAttempt++;
        _connecting = null;
        if (track.IsStream) { ConnectStream(track, outgoingFade); return; }

        int handle = TakePreload(track.Id) ?? OpenStream(track.Path, prescan: true);
        if (handle == 0)
        {
            Skip(track, outgoingFade, Bass.LastError.ToString());
            return;
        }

        double length = Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle, PositionFlags.Bytes));
        // The prescanned length is exact; the one from adding the file may be an estimate.
        if (Math.Abs(track.Duration - length) > 0.001)
        {
            track = track with { Duration = length };
            int index = _playlist.FindIndex(t => t.Id == track.Id);
            if (index >= 0) _playlist[index] = track;
        }
        if (track.Start > 0) Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, track.Start));
        Begin(track, handle, length, outgoingFade);
    }

    /// <summary>An item that can't be played: mark it played and, in AUTO, go on to the one after it.</summary>
    void Skip(Track track, double? outgoingFade, string reason)
    {
        Emit($"Cannot play {track.Display}: {reason}; skipping");
        _played.Add(track.Id);
        _cursorIndex = _playlist.FindIndex(t => t.Id == track.Id);
        if (_autoAdvance && NextTrack() is { } after && after.Id != track.Id) StartTrack(after, outgoingFade);
        else PlaylistUpdated();
    }

    /// <summary>
    /// Connects to an internet stream off the engine thread (it can take seconds); whatever is on air keeps playing
    /// until it is ready. A later start or stop cancels it.
    /// </summary>
    void ConnectStream(Track track, double? outgoingFade)
    {
        int attempt = _connectAttempt;
        _connecting = track.Id;
        var connection = Connection(track);
        if (!connection.IsCompleted) Emit($"Connecting to {track.Title}…");
        connection.ContinueWith(t => Post(() =>
        {
            if (attempt != _connectAttempt) return; // superseded; PlaylistUpdated or Stop drops the connection
            _connecting = null;
            if (_streamConnection is { } c && c.Handle == t) _streamConnection = null; // taken: it's ours now
            if (t.IsCompletedSuccessfully) Begin(track, t.Result, track.Duration, outgoingFade);
            else Skip(track, outgoingFade, $"could not connect ({t.Exception?.InnerException?.Message})");
        }));
    }

    /// <summary>The connection to a stream item: the one already opened ahead of time, or a new one.</summary>
    Task<int> Connection(Track track)
    {
        if (_streamConnection is { } c && c.Id == track.Id) return c.Handle;
        DropConnection();
        var task = Task.Run(() =>
        {
            int handle = Bass.CreateStream(track.Path, 0, BassFlags.Decode | BassFlags.Float, null, IntPtr.Zero);
            return handle != 0 ? handle : throw new IOException(Bass.LastError.ToString());
        });
        _streamConnection = (track.Id, task);
        return task;
    }

    void DropConnection()
    {
        if (_streamConnection is not { } c) return;
        _streamConnection = null;
        c.Handle.ContinueWith(t => { if (t.IsCompletedSuccessfully) Bass.StreamFree(t.Result); });
    }

    /// <summary>In AUTO, opens the connection to a stream that is next up shortly before the current item reaches its mix point.</summary>
    void PreconnectStream()
    {
        if (!_autoAdvance || _connecting != null || _streamConnection != null) return;
        if (_current is not { } deck || deck.Track.IsOpenEnded || NextTrack() is not { IsStream: true } next) return;
        long bytes = BassMix.ChannelGetPosition(deck.Handle, PositionFlags.Bytes);
        if (bytes < 0) return;
        double mixAt = Math.Min(deck.Track.MixPoint(CrossfadeSeconds), deck.Track.IsStream ? deck.Track.End : deck.Length);
        if (mixAt - Bass.ChannelBytes2Seconds(deck.Handle, bytes) <= StreamConnectLeadSeconds) Connection(next);
    }

    /// <summary>Puts an opened item on air, taking over from the current one.</summary>
    void Begin(Track track, int handle, double length, double? outgoingFade)
    {
        if (_current is { } old)
        {
            // Manual changes while paused should not un-pause the old deck into a fade.
            if (State == PlayerState.Paused) FadeOutAndFree(old.Handle, 0);
            else if (outgoingFade is { } fade) FadeOutAndFree(old.Handle, fade);
            else _playingOut.Add(old.Handle); // keeps playing; its Cue Out (or end) sync frees it
        }

        // Mixer syncs can only be set on a channel that is already a mixer source,
        // so add it paused, attach the syncs, then let it go.
        if (!BassMix.MixerAddChannel(_graph.MusicBus, handle, BassFlags.AutoFree | BassFlags.MixerChanPause))
        {
            Emit($"Mixer rejected {track.Display}: {Bass.LastError}");
            Bass.StreamFree(handle);
            return;
        }

        // For a stream, End means the connection dropped.
        BassMix.ChannelSetSync(handle, SyncFlags.End | SyncFlags.Mixtime, 0, _endSync);
        var deck = track.IsStream ? ArmStream(handle, track) : Arm(handle, track, length);
        BassMix.ChannelFlags(handle, BassFlags.Default, BassFlags.MixerChanPause);

        _current = deck;
        _state = (int)PlayerState.Playing;
        _played.Add(track.Id);
        _cursorIndex = _playlist.FindIndex(t => t.Id == track.Id);

        if (_encoder is var enc and not 0) BassEnc.CastSetTitle(enc, track.Display, null);
        Emit($"On air: {track.Display}");

        PlaylistUpdated();
    }

    /// <summary>Sets the Mix and Cue Out syncs on a deck (it must already be a mixer source).</summary>
    Deck Arm(int handle, Track track, double length)
    {
        double end = Math.Min(track.End, length);
        double mixAt = Math.Min(track.MixPoint(CrossfadeSeconds), end);

        int mixSync = 0, cueOutSync = 0;
        // With the mix point at the end there is no overlap: Cue Out (or the end of the file) starts the next item.
        if (mixAt < end - 0.01)
            mixSync = BassMix.ChannelSetSync(handle, SyncFlags.Position | SyncFlags.Mixtime, Bass.ChannelSeconds2Bytes(handle, mixAt), _crossfadeSync);
        if (end < length - 0.01)
            cueOutSync = BassMix.ChannelSetSync(handle, SyncFlags.Position | SyncFlags.Mixtime, Bass.ChannelSeconds2Bytes(handle, end), _cueOutSync);

        // A Mix marker means "start the next one here, let this one finish"; the default crossfade fades it out by Cue Out.
        double? mixFade = track.Cues?.Mix != null ? null : end - mixAt;
        return new Deck(handle, track, length, mixSync, cueOutSync, mixFade);
    }

    /// <summary>
    /// A stream has no length to put syncs on, so its play time runs on timers: the next item starts one crossfade
    /// before the end, and at the end it goes (like Cue Out). An open-ended stream waits for Stop or Next.
    /// </summary>
    Deck ArmStream(int handle, Track track)
    {
        if (track.IsOpenEnded) return new Deck(handle, track, 0, 0, 0, CrossfadeSeconds);
        double mixAt = track.MixPoint(CrossfadeSeconds);
        if (mixAt < track.End) After(mixAt, () => OnCrossfadePoint(handle));
        // Only while still on air: once the next item has started, its crossfade fades this one out.
        After(track.End, () => { if (_current?.Handle == handle) OnCueOut(handle); });
        return new Deck(handle, track, track.Duration, 0, 0, track.End - mixAt);
    }

    /// <summary>Fades decks that are still playing out under the current item (the operator took over).</summary>
    void FadePlayingOut(double seconds)
    {
        foreach (int h in _playingOut) FadeOutAndFree(h, seconds);
        _playingOut.Clear();
    }

    void StopInternal()
    {
        _resumeAfterSignal = false;
        _connectAttempt++;
        _connecting = null;
        DropConnection();
        FadePlayingOut(StopFadeSeconds);
        if (_current is { } deck) FadeOutAndFree(deck.Handle, State == PlayerState.Paused ? 0 : StopFadeSeconds);
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    void SetPaused(bool paused)
    {
        if (_current is not { } deck) return;
        if (paused) FadePlayingOut(StopFadeSeconds);
        BassMix.ChannelFlags(deck.Handle, paused ? BassFlags.MixerChanPause : BassFlags.Default, BassFlags.MixerChanPause);
        _state = (int)(paused ? PlayerState.Paused : PlayerState.Playing);
    }

    void FadeOutAndFree(int handle, double seconds)
    {
        if (seconds <= 0) { Bass.StreamFree(handle); return; }
        Bass.ChannelSlideAttribute(handle, ChannelAttribute.Volume, 0, (int)(seconds * 1000));
        After(seconds + 0.2, () => Bass.StreamFree(handle));
    }

    // ───────────────────────── Time signal ─────────────────────────

    /// <summary>Shortly before each top of the hour, schedules the time signal so the hour falls exactly on the hour.</summary>
    void CheckTimeSignal()
    {
        if (_timeSignal.Mode == TimeSignalMode.Off) return;
        var now = DateTime.Now;
        // The first hour that still leaves time to prepare (switched on at 59:58, it starts with the next hour).
        _signalHour ??= TopOfHour(now.AddSeconds(_signalHourAt + SignalPrepSeconds));
        double untilHour = (_signalHour.Value - now).TotalSeconds;
        if (untilHour > _signalHourAt + SignalPrepSeconds) return;
        var hour = _signalHour.Value;
        _signalHour = hour.AddHours(1);
        if (untilHour < _signalHourAt) return; // too late for this one (the computer was asleep, the clock jumped)
        StartTimeSignal(hour, untilHour);
    }

    static DateTime TopOfHour(DateTime after) => new DateTime(after.Year, after.Month, after.Day, after.Hour, 0, 0, after.Kind).AddHours(1);

    void StartTimeSignal(DateTime hour, double untilHour)
    {
        int h = TimeSignal.Open(_timeSignal.File, Emit, out double hourAt, out double length);
        if (h == 0) { Emit($"Time signal: {Bass.LastError}"); return; }

        // Mixed in with a delay so it is heard, after what is still queued on the card, exactly on time.
        double cartLatency = Bass.ChannelBytes2Seconds(_graph.CartBus, _graph.CartDelay);
        double startIn = Math.Max(0, untilHour - hourAt - cartLatency);
        if (!BassMix.MixerAddChannel(_graph.CartBus, h, BassFlags.AutoFree, Bass.ChannelSeconds2Bytes(_graph.CartBus, startIn), 0))
        {
            Emit($"Time signal: {Bass.LastError}");
            Bass.StreamFree(h);
            return;
        }
        Emit($"Time signal for {hour:HH:mm}");
        if (_timeSignal.Mode != TimeSignalMode.Stop) return;

        double musicLatency = Bass.ChannelBytes2Seconds(_graph.MusicBus, _graph.MusicDelay);
        double signalStart = untilHour - hourAt; // when it is heard
        After(signalStart - SignalFadeSeconds - musicLatency, TakeOffAirForSignal);
        After(signalStart + length - musicLatency, ResumeAfterSignal);
    }

    /// <summary>Stop mode: fades out the item on air and any carts, so the time signal is heard on its own.</summary>
    void TakeOffAirForSignal()
    {
        foreach (int cart in _carts.Keys) StopCart(cart);
        bool playing = State == PlayerState.Playing && (_current != null || _connecting != null);
        if (!playing) return; // stopped or paused: the operator is in charge
        _resumeAfterSignal = _autoAdvance;
        _connectAttempt++; // a stream still connecting is not put on air; the next item takes its connection
        _connecting = null;
        FadePlayingOut(SignalFadeSeconds);
        if (_current is { } deck) FadeOutAndFree(deck.Handle, SignalFadeSeconds);
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    void ResumeAfterSignal()
    {
        if (!_resumeAfterSignal) return;
        _resumeAfterSignal = false;
        // Not if the operator started something during the signal.
        if (_current == null && _connecting == null && NextTrack() is { } next) StartTrack(next, 0);
    }

    /// <summary>Publish a snapshot and pre-open the next file so the crossfade point never waits on disk.</summary>
    void PlaylistUpdated()
    {
        var next = NextTrack();
        if (_preload is { } p && p.Id != next?.Id)
        {
            Bass.StreamFree(p.Handle);
            _preload = null;
        }
        if (_streamConnection is { } sc && sc.Id != next?.Id) DropConnection();
        if (next != null && !next.IsStream && _preload == null && _current != null)
        {
            int h = OpenStream(next.Path, prescan: true);
            if (h != 0) _preload = (next.Id, h);
            _waveforms.LoadAsync(next.Path); // warm the cache so the waveform is ready at the transition
        }

        PlaylistChanged?.Invoke(new PlaylistSnapshot(
            _playlist.ToList(), _current?.Track.Id, next?.Id, _played.ToHashSet()));
    }

    int? TakePreload(Guid id)
    {
        if (_preload is not { } p) return null;
        _preload = null;
        if (p.Id == id) return p.Handle;
        Bass.StreamFree(p.Handle);
        return null;
    }

    static int OpenStream(string path, bool prescan)
    {
        var flags = BassFlags.Decode | BassFlags.Float;
        if (prescan) flags |= BassFlags.Prescan;
        return Bass.CreateStream(path, 0, 0, flags);
    }

    /// <summary>Like <see cref="Probe"/>, but a file that fails is reported and skipped instead of failing the whole add.</summary>
    Track? SafeProbe(string path)
    {
        try { return Probe(path); }
        catch (Exception ex)
        {
            Emit($"Skipped {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    Track? Probe(string path)
    {
        int h = OpenStream(path, prescan: false);
        if (h == 0)
        {
            Emit($"Unsupported or unreadable: {Path.GetFileName(path)} ({Bass.LastError})");
            return null;
        }
        try
        {
            double len = Bass.ChannelBytes2Seconds(h, Bass.ChannelGetLength(h, PositionFlags.Bytes));
            TagReader? tags = null;
            try { tags = TagReader.Read(h); }
            catch (Exception ex)
            {
                // Malformed tags (e.g. a corrupt ID3v2 frame) can make the tag parser throw; fall back to the filename.
                Emit($"Unreadable tags, using the filename: {Path.GetFileName(path)} ({ex.Message})");
            }
            string title = tags?.Title ?? "";
            string artist = tags?.Artist ?? "";
            if (string.IsNullOrWhiteSpace(title))
            {
                // Fall back to "Artist - Title.mp3" style filenames.
                var name = Path.GetFileNameWithoutExtension(path);
                var parts = name.Split(" - ", 2);
                (artist, title) = parts.Length == 2 ? (parts[0], parts[1]) : ("", name);
            }
            return new Track(Guid.NewGuid(), path, artist.Trim(), title.Trim(), len, TrackClassifier.Classify(path), _cues.Get(path));
        }
        finally
        {
            Bass.StreamFree(h);
        }
    }

    static void LoadPlugins()
    {
        // Windows and macOS decode AAC/M4A through the OS; Linux needs bass_aac.
        string? file = OperatingSystem.IsLinux() ? "libbass_aac.so" : null;
        if (file == null) return;
        string full = Path.Combine(AppContext.BaseDirectory, file);
        if (File.Exists(full)) Bass.PluginLoad(full);
    }

    void Emit(string message) => Log?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}
