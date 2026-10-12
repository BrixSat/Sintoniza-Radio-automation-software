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

    readonly BlockingCollection<Action> _commands = new();
    readonly List<(DateTime Due, Action Action)> _timers = new();
    readonly Thread _thread;

    // Strong references: BASS holds raw function pointers to these.
    readonly SyncProcedure _crossfadeSync;
    readonly SyncProcedure _endSync;

    readonly AudioGraph _graph;
    readonly WaveformLoader _waveforms = new();

    // Engine-thread state.
    readonly List<Track> _playlist = new();
    readonly HashSet<Guid> _played = new();
    int _cursorIndex = -1; // index of the last started item; survives removal of that item
    (Guid Id, int Handle)? _preload;

    // Shared with polling readers; written only on the engine thread.
    volatile Deck? _current;
    volatile int _state = (int)PlayerState.Stopped;
    volatile int _encoder;
    volatile int _streamState = (int)StreamState.Off;
    volatile string? _streamError;

    volatile bool _autoAdvance = true;
    double _crossfadeSeconds = 5;

    sealed record Deck(int Handle, Track Track, double Length);

    public event Action<PlaylistSnapshot>? PlaylistChanged;
    public event Action<string>? Log;

    /// <param name="routing">Where the music and cart buses go.</param>
    /// <param name="forceDevice">Send every bus to this BASS device regardless of routing (0 = "no sound", for testing).</param>
    public PlayoutEngine(AudioRouting routing, int? forceDevice = null)
    {
        LoadPlugins();

        _graph = new AudioGraph(forceDevice);
        _graph.Apply(routing);

        _crossfadeSync = (_, channel, _, _) => _commands.Add(() => OnCrossfadePoint(channel));
        _endSync = (_, channel, _, _) => _commands.Add(() => OnTrackEnd(channel));

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

    /// <summary>Short 1 kHz tone on a card/channel pair, to find the right desk fader.</summary>
    public void PlayTestTone(OutputRoute route) => _graph.TestTone(route);

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
            var tracks = list.Select(Probe).OfType<Track>().ToList();
            int at = insertAt < 0 || insertAt > _playlist.Count ? _playlist.Count : insertAt;
            _playlist.InsertRange(at, tracks);
            if (at <= _cursorIndex) _cursorIndex += tracks.Count;
            PlaylistUpdated();
        });
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
            if (t != null) StartTrack(t);
            return;
        }
        if (_current != null && State == PlayerState.Paused) { SetPaused(false); return; }
        if (_current != null) return;
        // At the end of the list, Play starts again from the top.
        if ((NextTrack() ?? _playlist.FirstOrDefault()) is { } next) StartTrack(next);
    });

    /// <summary>Crossfade into the next playlist item immediately.</summary>
    public void Next() => Post(() =>
    {
        if (NextTrack() is { } next) StartTrack(next);
        else StopInternal();
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
        return h;
    }

    public void StopCart(int handle)
    {
        if (handle == 0) return;
        Bass.ChannelSlideAttribute(handle, ChannelAttribute.Volume, 0, (int)(CartStopFadeSeconds * 1000));
        Post(() => After(CartStopFadeSeconds + 0.2, () => Bass.StreamFree(handle)));
    }

    /// <summary>Seconds left on a cart, or null when it has finished.</summary>
    public double? CartRemaining(int handle)
    {
        if (handle == 0 || Bass.ChannelIsActive(handle) == PlaybackState.Stopped) return null;
        long len = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
        if (len < 0) return null;
        // Until the mixer has rendered the first block there is no position yet.
        long pos = Math.Max(0, BassMix.ChannelGetPosition(handle, PositionFlags.Bytes, _graph.CartDelay));
        return Bass.ChannelBytes2Seconds(handle, len - pos);
    }

    /// <summary>Amplitude envelope for drawing; decoded off the on-air path and cached.</summary>
    public Task<Waveform?> LoadWaveformAsync(string path) => _waveforms.LoadAsync(path);

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

        return new PlayerStatus(State, deck?.Track, pos, len, left, right,
            (StreamState)_streamState, _streamError);
    }

    public PlayerState State => (PlayerState)_state;

    public void StartStreaming(StreamSettings s)
    {
        StopStreaming();
        _streamState = (int)StreamState.Connecting;
        _streamError = null;

        // CastInit blocks while connecting, so keep it off the engine thread.
        Task.Run(() =>
        {
            int enc = s.Format == StreamFormat.Mp3
                ? BassEnc_Mp3.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero)
                : BassEnc_Ogg.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero);
            if (enc == 0) { FailStream($"Encoder start failed: {Bass.LastError}"); return; }

            string mount = s.Mount.StartsWith('/') ? s.Mount : "/" + s.Mount;
            string server = s.Shoutcast ? $"{s.Host}:{s.Port}" : $"{s.Host}:{s.Port}{mount}";
            string mime = s.Format == StreamFormat.Mp3 ? BassEnc.MimeMp3 : BassEnc.MimeOgg;

            if (!BassEnc.CastInit(enc, server, s.Password, mime, s.StationName, s.Url, s.Genre, null, null, s.Bitrate, false))
            {
                var err = Bass.LastError;
                BassEnc.EncodeStop(enc);
                FailStream($"Could not connect to {server} ({err}); check host, port, mount and password");
                return;
            }

            _encoder = enc;
            _streamState = (int)StreamState.OnAir;
            Emit($"Streaming to {server} ({s.Format} {s.Bitrate} kbps)");
            if (_current?.Track is { } t) BassEnc.CastSetTitle(enc, t.Display, null);
        });
    }

    public void StopStreaming()
    {
        int enc = Interlocked.Exchange(ref _encoder, 0);
        if (enc != 0) { BassEnc.EncodeStop(enc); Emit("Streaming stopped"); }
        _streamState = (int)StreamState.Off;
    }

    public void Dispose()
    {
        StopStreaming();
        _commands.CompleteAdding();
        _thread.Join(2000);
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
        if (enc != 0 && BassEnc.EncodeIsActive(enc) == PlaybackState.Stopped)
        {
            _encoder = 0;
            FailStream("Connection to the streaming server was lost");
        }
    }

    void FailStream(string message)
    {
        _streamState = (int)StreamState.Failed;
        _streamError = message;
        Emit(message);
    }

    Track? NextTrack()
    {
        int i = _cursorIndex + 1;
        return i >= 0 && i < _playlist.Count ? _playlist[i] : null;
    }

    void OnCrossfadePoint(int handle)
    {
        if (_current?.Handle != handle || !_autoAdvance) return;
        if (NextTrack() is { } next) StartTrack(next);
    }

    void OnTrackEnd(int handle)
    {
        if (_current?.Handle != handle) return; // a faded-out deck finishing, nothing to do
        if (_autoAdvance && NextTrack() is { } next)
        {
            StartTrack(next);
            return;
        }
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    void StartTrack(Track track)
    {
        int handle = TakePreload(track.Id) ?? OpenStream(track.Path, prescan: true);
        if (handle == 0)
        {
            Emit($"Cannot play {track.Display}: {Bass.LastError}; skipping");
            _played.Add(track.Id);
            _cursorIndex = _playlist.FindIndex(t => t.Id == track.Id);
            if (_autoAdvance && NextTrack() is { } after && after.Id != track.Id) StartTrack(after);
            else PlaylistUpdated();
            return;
        }

        double length = Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle, PositionFlags.Bytes));
        double xfade = CrossfadeSeconds;

        if (_current is { } old)
        {
            // Manual changes while paused should not un-pause the old deck into a fade.
            if (State == PlayerState.Paused) FadeOutAndFree(old.Handle, 0);
            else FadeOutAndFree(old.Handle, Math.Max(xfade, StopFadeSeconds));
        }

        // Mixer syncs can only be set on a channel that is already a mixer source,
        // so add it paused, attach the syncs, then let it go.
        if (!BassMix.MixerAddChannel(_graph.MusicBus, handle, BassFlags.AutoFree | BassFlags.MixerChanPause))
        {
            Emit($"Mixer rejected {track.Display}: {Bass.LastError}");
            Bass.StreamFree(handle);
            return;
        }

        // Very short items (stingers) just play to the end instead of crossfading at once.
        if (xfade > 0 && length > xfade * 2)
        {
            long at = Bass.ChannelSeconds2Bytes(handle, length - xfade);
            BassMix.ChannelSetSync(handle, SyncFlags.Position | SyncFlags.Mixtime, at, _crossfadeSync);
        }
        BassMix.ChannelSetSync(handle, SyncFlags.End | SyncFlags.Mixtime, 0, _endSync);
        BassMix.ChannelFlags(handle, BassFlags.Default, BassFlags.MixerChanPause);

        _current = new Deck(handle, track, length);
        _state = (int)PlayerState.Playing;
        _played.Add(track.Id);
        _cursorIndex = _playlist.FindIndex(t => t.Id == track.Id);

        if (_encoder is var enc and not 0) BassEnc.CastSetTitle(enc, track.Display, null);
        Emit($"On air: {track.Display}");

        PlaylistUpdated();
    }

    void StopInternal()
    {
        if (_current is { } deck) FadeOutAndFree(deck.Handle, State == PlayerState.Paused ? 0 : StopFadeSeconds);
        _current = null;
        _state = (int)PlayerState.Stopped;
        PlaylistUpdated();
    }

    void SetPaused(bool paused)
    {
        if (_current is not { } deck) return;
        BassMix.ChannelFlags(deck.Handle, paused ? BassFlags.MixerChanPause : BassFlags.Default, BassFlags.MixerChanPause);
        _state = (int)(paused ? PlayerState.Paused : PlayerState.Playing);
    }

    void FadeOutAndFree(int handle, double seconds)
    {
        if (seconds <= 0) { Bass.StreamFree(handle); return; }
        Bass.ChannelSlideAttribute(handle, ChannelAttribute.Volume, 0, (int)(seconds * 1000));
        After(seconds + 0.2, () => Bass.StreamFree(handle));
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
        if (next != null && _preload == null && _current != null)
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
            var tags = TagReader.Read(h);
            string title = tags?.Title ?? "";
            string artist = tags?.Artist ?? "";
            if (string.IsNullOrWhiteSpace(title))
            {
                // Fall back to "Artist - Title.mp3" style filenames.
                var name = Path.GetFileNameWithoutExtension(path);
                var parts = name.Split(" - ", 2);
                (artist, title) = parts.Length == 2 ? (parts[0], parts[1]) : ("", name);
            }
            return new Track(Guid.NewGuid(), path, artist.Trim(), title.Trim(), len, TrackClassifier.Classify(path));
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
