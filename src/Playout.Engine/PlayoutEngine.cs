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
    const int MaxSilenceSkips = 3;         // after this many silent items in a row the fault is not in the files: stop skipping, keep warning
    const int MaxStreamRetryDelay = 60;    // seconds between attempts to reconnect to the streaming server, at most
    const double FillRetrySeconds = 5;     // how often to try again when fill music couldn't be added
    const int FillAttempts = 10;           // files tried at a time (some may be unreadable)
    const int KeepPlayed = 500;            // played items kept above the cursor when fill music grows the playlist
    const double EventFadeSeconds = 2;     // a hard scheduled event fades out what is on air over this
    const double BacktimeTolerance = 5;    // backtiming leaves an item this many seconds early or late alone
    const double BacktimeMinGap = 15;      // a smaller gap before a backtimed break isn't filled: it starts that much early
    const int BacktimeFillTries = 8;       // fill songs looked at for one that fits a gap

    readonly BlockingCollection<Action> _commands = new();
    readonly List<(DateTime Due, Action Action)> _timers = new();
    readonly Thread _thread;

    // Strong references: BASS holds raw function pointers to these.
    readonly SyncProcedure _crossfadeSync;
    readonly SyncProcedure _endSync;
    readonly SyncProcedure _cueOutSync;
    readonly DSPProcedure _gainDsp;

    readonly AudioGraph _graph;
    readonly Microphone _mic;
    readonly WaveformLoader _waveforms = new();
    readonly CueStore _cues;
    readonly LoudnessStore _loudnessStore;
    readonly LoudnessWorker _loudnessWorker;
    volatile LoudnessSettings _loudness = new();

    // Engine-thread state.
    readonly List<Track> _playlist = new();
    readonly HashSet<Guid> _played = new();
    int _cursorIndex = -1; // index of the last started item; survives removal of that item
    (Guid Id, int Handle)? _preload;
    int _connectAttempt;   // bumped by every start and stop, so a stream that connects too late is dropped
    Guid? _connecting;     // stream item that is due but still connecting; automation waits for it instead of starting it again
    (Guid Id, Task<int> Handle)? _streamConnection; // connection to a stream item, opened ahead of its start
    readonly List<int> _playingOut = new(); // decks past their Mix marker, finishing under the next item
    readonly HashSet<int> _fading = new();  // decks fading out before they are freed
    readonly HashSet<string> _tagWriting = new(); // files whose tags are being written: not opened ahead of time meanwhile
    readonly ConcurrentDictionary<int, byte> _carts = new(); // carts fired and not stopped (some may have ended)
    TimeSignalSettings _timeSignal = new();
    double _signalHourAt;    // where the hour falls in the signal (seconds)
    DateTime? _signalHour;   // the next top of the hour that gets the signal (local time)
    bool _resumeAfterSignal; // Stop mode took the item off air: start the next one when the signal ends
    SilenceSettings _silence = new();
    DateTime? _silentSince;  // when the output last went quiet while an item was playing
    int _silenceSkips;       // silent items skipped in a row
    Guid? _silenceReported;  // item whose silence has been logged (ASSISTED, or too many skips)
    Func<string?>? _fill;    // gives a file to add when the playlist runs out in AUTO
    readonly HashSet<string> _badFill = new(); // fill files that couldn't be opened: not tried again
    bool _waitingForFill;    // AUTO ran out with nothing to fill yet: start something as soon as there is
    DateTime _nextFillTry;
    DateTime _nextTimedCheck;      // items with a latest start are looked at twice a second
    DateTime _nextBacktimeFill;    // looking for a song to fill a gap before a backtimed break: not too often
    bool _backtimeFilling;         // that search is running (off the engine thread)
    Guid? _backtimeReported;       // the backtimed item whose unfillable gap has been reported

    // Shared with polling readers; written only on the engine thread.
    volatile Deck? _current;
    volatile int _state = (int)PlayerState.Stopped;
    volatile int _encoder;
    volatile int _streamState = (int)StreamState.Off;
    volatile string? _streamError;
    int _streamAttempt;
    readonly object _streamLock = new(); // bumped by every start and stop, so a connection that completes after Stop is dropped
    StreamSettings? _streamWanted;       // streaming is switched on: reconnect when the connection fails (guarded by _streamLock)
    int _streamRetries;                  // failed attempts in a row, for the wait before the next one
    double _silenceSeconds;              // dead air so far (Volatile)
    long _heartbeat = Environment.TickCount64; // last time the engine thread was free to take commands (Volatile)

    volatile bool _autoAdvance = true;
    double _crossfadeSeconds = 5;

    /// <param name="MixFade">How the deck fades once the next item starts at the mix point; null = it plays on to Cue Out (Mix marker).</param>
    /// <param name="Started">When it went on air (UTC).</param>
    /// <param name="GainDb">Loudness levelling applied to it.</param>
    sealed record Deck(int Handle, Track Track, double Length, int MixSync, int CueOutSync, double? MixFade, DateTime Started = default, double GainDb = 0);

    public event Action<PlaylistSnapshot>? PlaylistChanged;
    public event Action<string>? Log;

    /// <summary>An item went on air (raised on the engine thread: hand slow work, such as writing history, to another thread).</summary>
    public event Action<Track>? OnAir;

    /// <summary>
    /// An item left the air: the next one took over, or the player stopped. With when it went on air and when it left
    /// (UTC). Raised on the engine thread, like <see cref="OnAir"/>.
    /// </summary>
    public event Action<Track, DateTime, DateTime>? OffAir;

    /// <param name="routing">Where the music, cart and preview buses go.</param>
    /// <param name="cues">Cue points per file; applied when files are added.</param>
    /// <param name="forceDevice">Send every bus to this BASS device regardless of routing (0 = "no sound", for testing).</param>
    /// <param name="loudness">Measured loudness per file (loudness levelling); null = kept in the temp folder.</param>
    public PlayoutEngine(AudioRouting routing, CueStore cues, int? forceDevice = null, LoudnessStore? loudness = null)
    {
        LoadPlugins();
        _cues = cues;
        _loudnessStore = loudness ?? new LoudnessStore(Path.Combine(Path.GetTempPath(), "radio-playout-loudness.txt"));
        _loudnessWorker = new LoudnessWorker(_loudnessStore);

        _graph = new AudioGraph(forceDevice);
        _graph.Apply(routing);
        _mic = new Microphone(_graph, After, Emit);

        _crossfadeSync = (_, channel, _, _) => _commands.Add(() => OnCrossfadePoint(channel));
        _endSync = (_, channel, _, _) => _commands.Add(() => OnTrackEnd(channel));
        _cueOutSync = (_, channel, _, _) => _commands.Add(() => OnCueOut(channel));
        _gainDsp = ApplyGain;

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
        Emit(L.T("Audio outputs updated"));
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
            ApplyCues(path, cues);
            PlaylistUpdated();
        });
    }

    /// <summary>Cue points of these files changed in the cue store (set in another studio): apply them like <see cref="SetCues"/>.</summary>
    public void ReloadCues(IReadOnlyCollection<string> paths) => Post(() =>
    {
        foreach (var path in paths) ApplyCues(path, _cues.Get(path) ?? CuePoints.None);
        PlaylistUpdated();
    });

    void ApplyCues(string path, CuePoints cues)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int i = 0; i < _playlist.Count; i++)
            if (string.Equals(_playlist[i].Path, path, comparer))
                _playlist[i] = _playlist[i] with { Cues = cues.IsEmpty ? null : cues };

        if (_current is { } deck && string.Equals(deck.Track.Path, path, comparer))
        {
            if (deck.MixSync != 0) BassMix.ChannelRemoveSync(deck.Handle, deck.MixSync);
            if (deck.CueOutSync != 0) BassMix.ChannelRemoveSync(deck.Handle, deck.CueOutSync);
            _current = Arm(deck.Handle, deck.Track with { Cues = cues.IsEmpty ? null : cues }, deck.Length) with { Started = deck.Started, GainDb = deck.GainDb };
        }
    }

    /// <summary>
    /// Writes a file's tags (see <see cref="Tags.Write"/>) and shows the new artist and title on every playlist item of
    /// that file. Refused while the file can be heard (on air, playing out, on a cart): rewriting the tags can move
    /// the audio under the player. The file is written off the engine thread, so automation never waits for it.
    /// </summary>
    public Task SaveTagsAsync(string path, TrackTags tags, byte[]? cover)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var comparer = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Post(() =>
        {
            if (IsAudible(path, comparer))
            {
                done.SetException(new InvalidOperationException(L.T("This file is playing. Edit its tags once it has finished.")));
                return;
            }
            // The next item is opened ahead of time: let go of the file while it is rewritten.
            _tagWriting.Add(path);
            if (_preload is { } p && _playlist.Find(t => t.Id == p.Id) is { } pre && string.Equals(pre.Path, path, comparer))
            {
                Bass.StreamFree(p.Handle);
                _preload = null;
            }
            Task.Run(() => Tags.Write(path, tags, cover)).ContinueWith(write => Post(() =>
            {
                _tagWriting.Remove(path);
                if (write.IsCompletedSuccessfully && !string.IsNullOrWhiteSpace(tags.Title))
                    for (int i = 0; i < _playlist.Count; i++)
                        if (string.Equals(_playlist[i].Path, path, comparer))
                            _playlist[i] = _playlist[i] with { Artist = tags.Artist.Trim(), Title = tags.Title.Trim() };
                PlaylistUpdated(); // opens the next item again
                if (write.IsCompletedSuccessfully)
                {
                    Emit(L.F("Tags saved: {0}", Path.GetFileName(path)));
                    done.SetResult();
                }
                else done.SetException(write.Exception!.InnerException ?? write.Exception);
            }));
        });
        return done.Task;
    }

    /// <summary>The file is being heard: on air, still fading or playing out under the next item, or on a cart.</summary>
    bool IsAudible(string path, StringComparison comparer)
    {
        if (_current is { } deck && string.Equals(deck.Track.Path, path, comparer)) return true;
        foreach (int h in _playingOut.Concat(_fading).Concat(_carts.Keys))
            if (Bass.ChannelIsActive(h) != PlaybackState.Stopped && string.Equals(Bass.ChannelGetInfo(h).FileName, path, comparer))
                return true;
        return false;
    }

    // ───────────────────────── Public API (thread-safe, non-blocking) ─────────────────────────

    public bool AutoAdvance
    {
        get => _autoAdvance;
        set
        {
            _autoAdvance = value;
            Post(PlaylistUpdated); // switched to AUTO on the last item: fill the playlist now
        }
    }

    /// <summary>Sets up dead air detection.</summary>
    public void ConfigureSilence(SilenceSettings settings) => Post(() =>
    {
        _silence = settings;
        _silentSince = null;
    });

    /// <summary>
    /// Where music comes from when the playlist runs out in AUTO: called on the engine thread for a file to add
    /// (null = nothing). Null turns it off: the player stops at the end of the playlist.
    /// </summary>
    public void ConfigureFill(Func<string?>? source) => Post(() =>
    {
        _fill = source;
        _badFill.Clear();
        PlaylistUpdated();
    });

    /// <summary>Sets up loudness levelling. Switched on, the playlist's files are measured in the background.</summary>
    public void ConfigureLoudness(LoudnessSettings settings) => Post(() =>
    {
        _loudness = settings;
        if (!settings.Enabled) return;
        foreach (var t in _playlist.Skip(Math.Max(0, _cursorIndex))) if (!t.IsStream) _loudnessWorker.Add(t.Path);
    });

    /// <summary>Measures these files' loudness in the background (carts, scheduled items), if levelling is on.</summary>
    public void MeasureLoudness(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        // On the engine thread: after a ConfigureLoudness posted just before.
        Post(() =>
        {
            if (!_loudness.Enabled) return;
            foreach (var path in list) _loudnessWorker.Add(path);
        });
    }

    /// <summary>A file's measured loudness, or null if it hasn't been measured (yet).</summary>
    public LoudnessInfo? GetLoudness(string path) => _loudnessStore.Get(path);

    /// <summary>
    /// A scheduled event (or ad break): puts its items in the playlist. Hard: after the item on air, and the first
    /// starts now (what is on air fades out). Soft: they follow the item on air (after other events still waiting),
    /// with a <see cref="EventTiming.MaxDelay"/> at most that late. EndAt: placed where the playlist will be when it
    /// has to start to end on time, and the playlist before it is backtimed (see <see cref="Backtime"/>). With the
    /// player stopped or paused they wait as next, for the operator.
    /// </summary>
    public void InsertEvent(string label, IReadOnlyList<ScheduledItem> items, EventTiming timing) => Post(() =>
    {
        var tracks = Scheduled(label, items);
        if (tracks.Count == 0)
        {
            Emit(L.F("{0}: nothing to play", label));
            return;
        }
        if (_loudness.Enabled) foreach (var t in tracks) if (!t.IsStream) _loudnessWorker.Add(t.Path);
        bool playing = State == PlayerState.Playing;

        if (timing is { Start: EventStart.EndAt, EndsAt: { } endsAt })
        {
            var startAt = endsAt - TimeSpan.FromSeconds(ChainLength(tracks));
            tracks[0] = tracks[0] with { StartBy = startAt, Backtime = true };
            double left = (startAt - DateTime.UtcNow).TotalSeconds;
            if (playing && left <= BacktimeTolerance)
            {
                // Already due (the app started late): now, ending late by as much.
                InsertAfterOnAir(tracks, afterWaiting: false);
                Emit(L.F("{0}: on air ({1:0} s late to end on time)", label, -left));
                FadePlayingOut(EventFadeSeconds);
                StartTrack(tracks[0], _current == null ? 0 : EventFadeSeconds);
                return;
            }
            int at = playing && _autoAdvance ? BacktimePosition(left, startAt) : -1;
            if (at < 0) InsertAfterOnAir(tracks, afterWaiting: true);
            else
            {
                _playlist.InsertRange(at, tracks);
                if (at <= _cursorIndex) _cursorIndex += tracks.Count;
            }
            Emit(L.F("{0}: starts at {1:HH:mm:ss} to end at {2:HH:mm:ss}", label, startAt.ToLocalTime(), endsAt.ToLocalTime()));
            PlaylistUpdated();
            return;
        }

        bool hard = timing.Start == EventStart.Hard;
        if (!hard && timing.MaxDelay is { } delay && delay > TimeSpan.Zero)
            tracks[0] = tracks[0] with { StartBy = DateTime.UtcNow + delay };
        InsertAfterOnAir(tracks, afterWaiting: !hard);
        if (playing && (hard || (_current == null && _connecting == null)))
        {
            Emit(L.F("{0}: on air", label));
            FadePlayingOut(EventFadeSeconds);
            StartTrack(tracks[0], _current == null ? 0 : EventFadeSeconds);
        }
        else
        {
            Emit(playing ? L.F("{0}: plays next", label)
                : State == PlayerState.Paused ? L.F("{0}: waiting as next (the player is paused)", label)
                : L.F("{0}: waiting as next (the player is stopped)", label));
            PlaylistUpdated();
        }
    });

    /// <summary>
    /// Inserts items after the item on air (or after a stream that is about to go on air); with
    /// <paramref name="afterWaiting"/>, also after scheduled items still waiting there, so events play in the order
    /// they were due.
    /// </summary>
    void InsertAfterOnAir(List<Track> tracks, bool afterWaiting)
    {
        int at = _cursorIndex + 1;
        if (_connecting is { } connecting && _playlist.FindIndex(t => t.Id == connecting) is var c and >= 0) at = c + 1;
        at = Math.Clamp(at, 0, _playlist.Count);
        if (afterWaiting)
            while (at < _playlist.Count && _playlist[at].Event != null && !_played.Contains(_playlist[at].Id)) at++;
        _playlist.InsertRange(at, tracks);
        if (at <= _cursorIndex) _cursorIndex += tracks.Count;
    }

    /// <summary>Adds items a scheduled event put in the playlist, with their label as it was (a saved playlist).</summary>
    public void AddScheduled(string label, IReadOnlyList<ScheduledItem> items, int insertAt = -1) => Post(() =>
        Insert(Scheduled(label, items), insertAt));

    /// <summary>Opens the items of a scheduled event (files that can't be read are left out), labelled with it.</summary>
    List<Track> Scheduled(string label, IReadOnlyList<ScheduledItem> items)
    {
        var tracks = new List<Track>();
        foreach (var item in items)
        {
            Volatile.Write(ref _heartbeat, Environment.TickCount64);
            var track = item.StreamName != null
                ? new Track(Guid.NewGuid(), item.Path.Trim(), "", item.StreamName, Math.Max(0, item.StreamSeconds), TrackKind.Stream)
                : SafeProbe(item.Path);
            if (track != null)
                tracks.Add(track with
                {
                    Kind = item.Kind ?? track.Kind, Event = label, Advertiser = item.Advertiser,
                    Lock = item.Lock, StartBy = item.StartBy, Backtime = item.Backtime,
                });
        }
        return tracks;
    }

    /// <summary>
    /// After a restart: the items up to <paramref name="index"/> (the one that was on air) count as played, and with
    /// <paramref name="play"/> the one after it starts, so a file that brought the app down isn't played again.
    /// </summary>
    /// <returns>Completes once done; the playlist added before it is then complete.</returns>
    public Task Resume(int index, bool play)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { ResumeAt(index, play); }
            finally { done.SetResult(); }
        });
        return done.Task;
    }

    void ResumeAt(int index, bool play)
    {
        index = Math.Min(index, _playlist.Count - 1);
        for (int i = 0; i <= index; i++) _played.Add(_playlist[i].Id);
        _cursorIndex = index;
        PlaylistUpdated();
        if (!play || _current != null) return;
        Emit(L.T("Carrying on after a restart"));
        if (NextTrack() is { } next) StartTrack(next, 0);
        else StartFill();
    }

    /// <summary>False when the engine thread has been stuck for <paramref name="seconds"/>: automation has stopped.</summary>
    public bool IsResponsive(double seconds) => Environment.TickCount64 - Volatile.Read(ref _heartbeat) < seconds * 1000;

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
            // Adding a large folder can take a while: still alive.
            Insert(list.Select(p => { Volatile.Write(ref _heartbeat, Environment.TickCount64); return SafeProbe(p); }).OfType<Track>().ToList(), insertAt);
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
        if (_loudness.Enabled) foreach (var t in tracks) if (!t.IsStream) _loudnessWorker.Add(t.Path);
        PlaylistUpdated();
    }

    public void Remove(Guid id) => Post(() =>
    {
        int i = _playlist.FindIndex(t => t.Id == id);
        if (i < 0) return;
        if (IsKept(_playlist[i]))
        {
            Emit(_playlist[i].Lock == ItemLock.Locked
                ? L.F("{0} can't be removed: the schedule locked it ({1})", _playlist[i].Display, _playlist[i].Event)
                : L.F("{0} can't be removed: the schedule only lets it be moved ({1})", _playlist[i].Display, _playlist[i].Event));
            return;
        }
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
        foreach (var locked in _playlist.Where(t => moving.Contains(t.Id) && t.Lock == ItemLock.Locked && !_played.Contains(t.Id)).ToList())
        {
            Emit(L.F("{0} can't be moved: the schedule locked it ({1})", locked.Display, locked.Event));
            moving.Remove(locked.Id);
        }
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

    /// <summary>Empties the playlist, except the item on air and scheduled items the operator may not remove.</summary>
    public void Clear() => Post(() =>
    {
        var current = _current?.Track;
        var kept = _playlist.Where(IsKept).ToList();
        _playlist.Clear();
        _played.Clear();
        if (current != null) { _playlist.Add(current); _played.Add(current.Id); _cursorIndex = 0; }
        else _cursorIndex = -1;
        _playlist.AddRange(kept.Where(t => t.Id != current?.Id));
        if (kept.Count > 0) Emit(L.F("Playlist cleared; {0} scheduled items the schedule keeps are still there", kept.Count));
        PlaylistUpdated();
    });

    /// <summary>An item still to play that the schedule doesn't let the operator remove (locked or movable only).</summary>
    bool IsKept(Track t) => t.Lock != ItemLock.Free && !_played.Contains(t.Id);

    /// <summary>Start playback. With an id, crossfade into that item now; otherwise resume or start the next item.</summary>
    public void Play(Guid? id = null) => Post(() =>
    {
        if (id is { } target)
        {
            var t = _playlist.FirstOrDefault(x => x.Id == target);
            if (t == null) return;
            CutShort(L.T("started another item"));
            KeepSkipped(t);
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
        CutShort(L.T("pressed Next"));
        if (NextTrack() is not { } next) { StopInternal(); return; }
        FadePlayingOut(ManualFade);
        StartTrack(next, ManualFade);
    });

    public void Stop() => Post(() =>
    {
        CutShort(L.T("stopped the player"));
        StopInternal();
    });

    /// <summary>The operator takes a locked item off air early: said in the log (the as-run log has how long it played).</summary>
    void CutShort(string how)
    {
        if (_current?.Track is { Lock: ItemLock.Locked } t) Emit(L.F("The operator {0} during {1}, which the schedule locked ({2})", how, t.Display, t.Event));
    }

    /// <summary>
    /// Before jumping to <paramref name="target"/>: the items still to play that it jumps over and the operator may
    /// not remove (locked, movable) are moved after it (and the rest of its event), so they still play.
    /// </summary>
    void KeepSkipped(Track target)
    {
        int ti = _playlist.FindIndex(t => t.Id == target.Id);
        if (ti <= _cursorIndex + 1) return;
        var skipped = _playlist.GetRange(_cursorIndex + 1, ti - _cursorIndex - 1).Where(IsKept).ToList();
        if (skipped.Count == 0) return;
        var ids = skipped.Select(t => t.Id).ToHashSet();
        _playlist.RemoveAll(t => ids.Contains(t.Id));
        int at = _playlist.FindIndex(t => t.Id == target.Id) + 1;
        while (at < _playlist.Count && target.Event != null && _playlist[at].Event == target.Event && !_played.Contains(_playlist[at].Id)) at++;
        _playlist.InsertRange(at, skipped);
        Emit(L.F("{0} scheduled items jumped over stay in the playlist, after {1}", skipped.Count, target.Display));
    }

    public void TogglePause() => Post(() =>
    {
        if (_current == null) return;
        SetPaused(State != PlayerState.Paused);
    });

    /// <summary>Fire a cart (jingle/ID) over whatever is on air. Returns a handle for <see cref="StopCart"/>.</summary>
    public int FireCart(string path)
    {
        int h = OpenStream(path, prescan: false);
        if (h == 0) { Emit(L.F("Cart failed: {0} ({1})", Path.GetFileName(path), Bass.LastError)); return 0; }
        SetGain(h, GainFor(path, isStream: false));
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
            (StreamState)_streamState, _streamError, Volatile.Read(ref _silenceSeconds));
    }

    public PlayerState State => (PlayerState)_state;

    /// <summary>Connects to the streaming server. Until <see cref="StopStreaming"/>, a lost or failed connection is retried.</summary>
    public void StartStreaming(StreamSettings s)
    {
        StopStreaming();
        lock (_streamLock)
        {
            _streamWanted = s;
            _streamRetries = 0;
        }
        ConnectStreaming(s);
    }

    /// <param name="retryOf">A reconnect: only if streaming is still wanted and nothing restarted it since this attempt.</param>
    void ConnectStreaming(StreamSettings s, int? retryOf = null)
    {
        int attempt;
        lock (_streamLock)
        {
            // Checked and claimed in one go, so a Stop at the same moment can't be overtaken by the reconnect.
            if (retryOf is { } failed && (failed != _streamAttempt || _streamWanted != s)) return;
            attempt = ++_streamAttempt;
            _streamState = (int)StreamState.Connecting;
            _streamError = null;
        }

        // CastInit blocks while connecting, so keep it off the engine thread.
        Task.Run(() =>
        {
            int enc = s.Format == StreamFormat.Mp3
                ? BassEnc_Mp3.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero)
                : BassEnc_Ogg.Start(_graph.Program, $"-b {s.Bitrate}", EncodeFlags.Default, null, IntPtr.Zero);
            if (enc == 0) { FailStream(L.F("Encoder start failed: {0}", Bass.LastError), attempt); return; }

            string mount = s.Mount.StartsWith('/') ? s.Mount : "/" + s.Mount;
            string server = s.Shoutcast ? $"{s.Host}:{s.Port}" : $"{s.Host}:{s.Port}{mount}";
            string mime = s.Format == StreamFormat.Mp3 ? BassEnc.MimeMp3 : BassEnc.MimeOgg;

            if (!BassEnc.CastInit(enc, server, s.Password, mime, s.StationName, s.Url, s.Genre, null, null, s.Bitrate, false))
            {
                var err = Bass.LastError;
                BassEnc.EncodeStop(enc);
                FailStream(L.F("Could not connect to {0} ({1}); check host, port, mount and password", server, err), attempt);
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
                _streamRetries = 0;
            }
            Emit(L.F("Streaming to {0} ({1} {2} kbps)", server, s.Format, s.Bitrate));
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
            _streamWanted = null;
            Interlocked.Increment(ref _streamAttempt);
            connecting = _streamState == (int)StreamState.Connecting;
            enc = Interlocked.Exchange(ref _encoder, 0);
            _streamState = (int)StreamState.Off;
            _streamError = null;
        }
        if (enc != 0) { BassEnc.EncodeStop(enc); Emit(L.T("Streaming stopped")); }
        else if (connecting) Emit(L.T("Streaming cancelled"));
    }

    public void Dispose()
    {
        StopStreaming();
        _commands.CompleteAdding();
        _thread.Join(2000);
        _loudnessWorker.Dispose();
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
            Volatile.Write(ref _heartbeat, Environment.TickCount64);
            if (_commands.TryTake(out var cmd, 50))
                Safe(cmd);
            RunTimers();
            Safe(CheckSilence);
            Safe(RetryFill);
            WatchStream();
            Safe(PreconnectStream);
            Safe(CheckTimeSignal);
            Safe(CheckTimed);
        }
    }

    void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Emit(L.F("Engine error: {0}", ex.Message)); }
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
            FailStream(L.T("Connection to the streaming server was lost"));
        }
    }

    /// <param name="attempt">The start this failure belongs to; ignored if streaming was stopped or restarted since.</param>
    void FailStream(string message, int? attempt = null)
    {
        StreamSettings? retry;
        int failed, delay;
        lock (_streamLock)
        {
            if (attempt is { } a && a != _streamAttempt) return;
            failed = _streamAttempt;
            retry = _streamWanted;
            delay = Math.Min(MaxStreamRetryDelay, 5 << Math.Min(_streamRetries++, 4)); // 5, 10, 20, 40, 60 s
            _streamState = (int)StreamState.Failed;
            _streamError = retry != null ? L.F("{0}. Trying again in {1} s", message, delay) : message;
        }
        Emit(_streamError);
        if (retry == null) return;
        Post(() => After(delay, () => ConnectStreaming(retry, failed)));
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
        if (_current.Track.IsStream) Emit(L.F("Stream ended: {0}", _current.Track.Title));
        if (_autoAdvance && NextTrack() is { } next)
        {
            if (next.Id == _connecting) { SetCurrent(null); PlaylistUpdated(); return; } // on air as soon as it connects
            StartTrack(next, 0);
            return;
        }
        SetCurrent(null);
        if (StartFill()) return;
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
                SetCurrent(null);
                PlaylistUpdated();
                return;
            }
            StartTrack(next, CueOutFadeSeconds);
            return;
        }
        FadeOutAndFree(handle, CueOutFadeSeconds);
        SetCurrent(null);
        if (StartFill()) return;
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
        Emit(L.F("Cannot play {0}: {1}; skipping", track.Display, reason));
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
        if (!connection.IsCompleted) Emit(L.F("Connecting to {0}…", track.Title));
        connection.ContinueWith(t => Post(() =>
        {
            if (attempt != _connectAttempt) return; // superseded; PlaylistUpdated or Stop drops the connection
            _connecting = null;
            if (_streamConnection is { } c && c.Handle == t) _streamConnection = null; // taken: it's ours now
            if (t.IsCompletedSuccessfully) Begin(track, t.Result, track.Duration, outgoingFade);
            else Skip(track, outgoingFade, L.F("could not connect ({0})", t.Exception?.InnerException?.Message));
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
            Emit(L.F("Mixer rejected {0}: {1}", track.Display, Bass.LastError));
            Bass.StreamFree(handle);
            return;
        }

        // For a stream, End means the connection dropped.
        BassMix.ChannelSetSync(handle, SyncFlags.End | SyncFlags.Mixtime, 0, _endSync);
        double gainDb = GainFor(track.Path, track.IsStream);
        SetGain(handle, gainDb);
        var deck = (track.IsStream ? ArmStream(handle, track) : Arm(handle, track, length)) with { Started = DateTime.UtcNow, GainDb = gainDb };
        BassMix.ChannelFlags(handle, BassFlags.Default, BassFlags.MixerChanPause);

        SetCurrent(deck);
        _state = (int)PlayerState.Playing;
        _waitingForFill = false;
        _silentSince = null; // a new item gets the full time before it counts as silent
        _played.Add(track.Id);
        _cursorIndex = _playlist.FindIndex(t => t.Id == track.Id);

        if (_encoder is var enc and not 0) BassEnc.CastSetTitle(enc, track.Display, null);
        Emit(L.F("On air: {0}", track.Display));
        OnAir?.Invoke(track);

        PlaylistUpdated();
    }

    /// <summary>Puts a deck on air (or none), and tells <see cref="OffAir"/> about the one it replaces.</summary>
    void SetCurrent(Deck? deck)
    {
        var old = _current;
        _current = deck;
        if (old != null && old.Track.Id != deck?.Track.Id) OffAir?.Invoke(old.Track, old.Started, DateTime.UtcNow);
    }

    // ───────────────────────── Loudness levelling ─────────────────────────

    /// <summary>
    /// How much to raise or lower a file (dB) to bring it to the target loudness: 0 when levelling is off, for streams,
    /// and for files not measured yet (they are queued, for next time).
    /// </summary>
    double GainFor(string path, bool isStream)
    {
        var s = _loudness;
        if (!s.Enabled || isStream) return 0;
        if (_loudnessStore.Get(path) is not { } info)
        {
            _loudnessWorker.Add(path);
            return 0;
        }
        double db = s.TargetLufs - info.Lufs;
        // Raised only as far as the peaks allow (-1 dBFS), and at most by the set amount.
        if (db > 0) db = Math.Min(db, Math.Max(0, Math.Min(s.MaxBoostDb, -1 - info.PeakDb)));
        return Math.Round(Math.Max(db, -30), 2);
    }

    /// <summary>Applies a gain to a channel's samples (a DSP: unlike the volume, it can raise them, and fades still work on top).</summary>
    void SetGain(int handle, double db)
    {
        if (Math.Abs(db) < 0.05) return;
        float gain = (float)Math.Pow(10, db / 20);
        Bass.ChannelSetDSP(handle, _gainDsp, BitConverter.SingleToInt32Bits(gain), 1);
    }

    static unsafe void ApplyGain(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        float gain = BitConverter.Int32BitsToSingle((int)user);
        var samples = new Span<float>((void*)buffer, length / sizeof(float));
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
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
        _waitingForFill = false;
        _connectAttempt++;
        _connecting = null;
        DropConnection();
        FadePlayingOut(StopFadeSeconds);
        if (_current is { } deck) FadeOutAndFree(deck.Handle, State == PlayerState.Paused ? 0 : StopFadeSeconds);
        SetCurrent(null);
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
        _fading.Add(handle);
        After(seconds + 0.2, () =>
        {
            _fading.Remove(handle);
            Bass.StreamFree(handle);
        });
    }

    // ───────────────────────── Dead air ─────────────────────────

    /// <summary>
    /// Watches the output while an item is playing. After the set time under the threshold, AUTO moves on to the next
    /// item; after a few silent items in a row the fault is elsewhere (sound card, output), so it only keeps warning.
    /// </summary>
    void CheckSilence()
    {
        // Waiting for fill music counts too: nothing is on air although the player should be playing.
        bool watching = _silence.Enabled && State == PlayerState.Playing && (_current != null || _waitingForFill)
                        && _connecting == null && !_mic.IsLive;
        if (watching)
        {
            var (left, right) = _graph.Levels();
            if (Math.Max(left, right) >= Math.Pow(10, _silence.ThresholdDb / 20))
            {
                watching = false;
                _silenceSkips = 0;
            }
        }
        if (!watching)
        {
            _silentSince = null;
            _silenceReported = null; // a new silence is reported again
            Volatile.Write(ref _silenceSeconds, 0);
            return;
        }

        var now = DateTime.UtcNow;
        _silentSince ??= now;
        double silent = (now - _silentSince.Value).TotalSeconds;
        Volatile.Write(ref _silenceSeconds, silent);
        if (silent < _silence.Seconds) return;

        if (_current is not { } deck)
        {
            if (_silenceReported != Guid.Empty) Emit(L.F("Silence on air for {0:0} s: waiting for fill music", silent));
            _silenceReported = Guid.Empty;
            return;
        }
        var track = deck.Track;
        if (!_autoAdvance || _silenceSkips >= MaxSilenceSkips || NextTrack() is not { } next || next.Id == _connecting)
        {
            if (_silenceReported != track.Id)
                Emit(_silenceSkips >= MaxSilenceSkips
                    ? L.F("Silence on air for {0:0} s after skipping {1} items: check the sound card and outputs", silent, _silenceSkips)
                    : L.F("Silence on air for {0:0} s during {1}", silent, track.Display));
            _silenceReported = track.Id;
            return;
        }
        Emit(L.F("Silence on air for {0:0} s during {1}: skipping to the next item", silent, track.Display));
        _silenceSkips++;
        _silentSince = null;
        FadePlayingOut(StopFadeSeconds);
        StartTrack(next, StopFadeSeconds);
    }

    /// <summary>
    /// In AUTO, once the item on air is the last one, adds one from the fill source so the playlist never runs out.
    /// True when it added one (and so has already published the playlist).
    /// </summary>
    bool Fill()
    {
        if (!_autoAdvance || (_current == null && _connecting == null) || NextTrack() != null) return false;
        if (FillTrack() is not { } track) return false;
        TrimPlayed();
        Emit(L.F("Playlist ran out: added {0}", track.Display));
        Insert([track], -1);
        return true;
    }

    /// <summary>
    /// AUTO ran out of items with nothing on air: start a fill song now if there is one, otherwise keep trying
    /// (<see cref="RetryFill"/>) until one can be played. False when there is no fill music, or not in AUTO.
    /// </summary>
    bool StartFill()
    {
        if (!_autoAdvance || _fill == null) return false;
        _waitingForFill = true;
        _state = (int)PlayerState.Playing; // still on duty: what's on air is coming
        if (FillTrack() is { } track)
        {
            TrimPlayed();
            Emit(L.F("Playlist ran out: added {0}", track.Display));
            Insert([track], -1);
            StartTrack(track, 0);
        }
        else
        {
            Emit(L.T("Playlist ran out and no fill music could be played yet; trying again"));
            PlaylistUpdated();
        }
        return true;
    }

    /// <summary>
    /// A few times a minute: the last item is on air and nothing could be added after it, or AUTO is waiting for
    /// fill music (folder still being read, network share away). Try again.
    /// </summary>
    void RetryFill()
    {
        if (_fill == null || !_autoAdvance || DateTime.UtcNow < _nextFillTry) return;
        _nextFillTry = DateTime.UtcNow.AddSeconds(FillRetrySeconds);
        if (_current != null && _connecting == null && NextTrack() == null) PlaylistUpdated(); // runs Fill
        else if (_waitingForFill && _current == null && _connecting == null && FillTrack() is { } track)
        {
            Emit(L.F("Fill music: {0}", track.Display));
            Insert([track], -1);
            StartTrack(track, 0);
        }
    }

    Track? FillTrack()
    {
        if (_fill == null) return null;
        for (int attempt = 0; attempt < FillAttempts; attempt++) // some files may be unreadable
        {
            string? path;
            try { path = _fill(); }
            catch (Exception ex) { Emit(L.F("Fill music: {0}", ex.Message)); return null; }
            if (path == null) return null;
            if (_badFill.Contains(path)) continue;
            if (SafeProbe(path) is { } track) return track;
            _badFill.Add(path);
        }
        return null;
    }

    /// <summary>
    /// Fill music adds an item per song, so a station left in AUTO for weeks would grow the playlist without end:
    /// keep only the last <see cref="KeepPlayed"/> played items above the one on air.
    /// </summary>
    void TrimPlayed()
    {
        int extra = _cursorIndex - KeepPlayed;
        if (extra <= 0) return;
        var gone = _playlist.Take(extra).ToList();
        if (gone.Any(t => !_played.Contains(t.Id) || t.Id == _current?.Track.Id)) return; // unplayed items: leave the list alone
        _playlist.RemoveRange(0, extra);
        foreach (var t in gone) _played.Remove(t.Id);
        _cursorIndex -= extra;
    }

    // ───────────────────────── Timed items and backtiming ─────────────────────────

    /// <summary>
    /// Twice a second while playing: the first upcoming item with a latest start (<see cref="Track.StartBy"/>). Once
    /// that time comes, what is on air fades out and it starts. Before that, in AUTO, a backtimed item gets the
    /// playlist before it adjusted so it starts on time.
    /// </summary>
    void CheckTimed()
    {
        var now = DateTime.UtcNow;
        if (now < _nextTimedCheck) return;
        _nextTimedCheck = now.AddSeconds(0.5);
        if (State != PlayerState.Playing) return;
        int ti = -1;
        for (int i = Math.Max(0, _cursorIndex + 1); i < _playlist.Count; i++)
            if (_playlist[i].StartBy != null && !_played.Contains(_playlist[i].Id)) { ti = i; break; }
        if (ti < 0) return;
        var timed = _playlist[ti];
        if (timed.Id == _connecting) return; // on its way
        double left = (timed.StartBy!.Value - now).TotalSeconds;
        if (left <= 0)
        {
            Emit(L.F("{0}: on air (its latest start time)", timed.Event ?? timed.Display));
            KeepSkipped(timed);
            FadePlayingOut(EventFadeSeconds);
            StartTrack(timed, _current == null ? 0 : EventFadeSeconds);
            return;
        }
        if (timed.Backtime && _autoAdvance && _connecting == null) Backtime(ti, left);
    }

    /// <summary>
    /// Backtiming: the item at <paramref name="ti"/> has to start in <paramref name="left"/> seconds. If the playlist
    /// would get there late, the last song before it that the operator may change goes after it (when that brings it
    /// closer); what is left over is faded at the time. If it would get there early, a fill song that fits the gap is
    /// put before it; a gap under <see cref="BacktimeMinGap"/> s is left (it starts that much early).
    /// </summary>
    void Backtime(int ti, double left)
    {
        if (Upcoming() is not { } upcoming) return;
        var timed = _playlist[ti];
        double error = upcoming.First(u => u.Index == ti).At - left; // > 0: late, < 0: early
        if (error > BacktimeTolerance)
        {
            for (int j = ti - 1; j > _cursorIndex; j--)
            {
                var song = _playlist[j];
                if (song.Lock != ItemLock.Free || song.Event != null || song.Kind != TrackKind.Music) continue;
                if (Math.Abs(error - Step(song)) >= error) continue; // wouldn't help
                _playlist.RemoveAt(j);
                int after = _playlist.FindIndex(t => t.Id == timed.Id) + 1;
                while (after < _playlist.Count && _playlist[after].Event == timed.Event && !_played.Contains(_playlist[after].Id)) after++;
                _playlist.Insert(after, song);
                Emit(L.F("Backtiming {0}: {1} moved after it ({2:0} s too long)", timed.Event, song.Display, error));
                PlaylistUpdated();
                return;
            }
            return; // what is on air then is faded at the time
        }
        if (error >= -BacktimeMinGap || _fill == null || _backtimeFilling || DateTime.UtcNow < _nextBacktimeFill) return;
        _nextBacktimeFill = DateTime.UtcNow.AddSeconds(10);
        double gap = -error;
        // Fill songs, then the one that fits best, opened off the engine thread (files can be slow to open).
        var candidates = new List<string>();
        for (int i = 0; i < BacktimeFillTries; i++)
        {
            string? path;
            try { path = _fill(); } catch { break; }
            if (path == null) break;
            if (!_badFill.Contains(path) && !candidates.Contains(path)) candidates.Add(path);
        }
        if (candidates.Count == 0) return;
        _backtimeFilling = true;
        var timedId = timed.Id;
        Task.Run(() => candidates.Select(SafeProbe).OfType<Track>().ToList()).ContinueWith(probed => Post(() =>
        {
            _backtimeFilling = false;
            int index = _playlist.FindIndex(t => t.Id == timedId);
            if (!probed.IsCompletedSuccessfully || index <= _cursorIndex || Upcoming() is not { } now) return;
            double stillLeft = (_playlist[index].StartBy!.Value - DateTime.UtcNow).TotalSeconds;
            double stillGap = stillLeft - now.First(u => u.Index == index).At;
            var best = probed.Result.Where(t => Step(t) <= stillGap + BacktimeTolerance).MaxBy(Step);
            if (best == null)
            {
                if (_backtimeReported != timedId)
                    Emit(L.F("Backtiming {0}: no fill song short enough for {1:0} s; it starts that much early", _playlist[index].Event, stillGap));
                _backtimeReported = timedId;
                return;
            }
            _playlist.Insert(index, best);
            if (_loudness.Enabled) _loudnessWorker.Add(best.Path);
            Emit(L.F("Backtiming {0}: added {1} to fill {2:0} s", _playlist[index + 1].Event, best.Display, stillGap));
            PlaylistUpdated();
        }));
    }

    /// <summary>How long after an item starts the next one does (in AUTO): its play length less its overlap with the next.</summary>
    double Step(Track t) => t.PlayLength - (t.End - t.MixPoint(CrossfadeSeconds));

    /// <summary>
    /// When each item after the one on air will start (seconds from now) if nothing changes, as the playlist's start
    /// times show; the last entry (index = count) is when the playlist runs out. Null when it can't be known: nothing
    /// on air, a stream connecting, or a stream that plays until Stop on air.
    /// </summary>
    List<(int Index, double At)>? Upcoming()
    {
        if (_current is not { } deck || deck.Track.IsOpenEnded || _connecting != null) return null;
        double position = deck.Track.IsStream
            ? (DateTime.UtcNow - deck.Started).TotalSeconds
            : Bass.ChannelBytes2Seconds(deck.Handle, Math.Max(0, BassMix.ChannelGetPosition(deck.Handle, PositionFlags.Bytes)));
        double mixAt = Math.Min(deck.Track.MixPoint(CrossfadeSeconds), deck.Track.IsStream ? deck.Track.End : deck.Length);
        double at = Math.Max(0, mixAt - position);
        var list = new List<(int, double)>();
        int first = _playlist.FindIndex(t => t.Id == deck.Track.Id) + 1;
        for (int i = first; i < _playlist.Count; i++)
        {
            list.Add((i, at));
            if (_playlist[i].IsOpenEnded) return list; // nothing after it has a time
            at += Step(_playlist[i]);
        }
        list.Add((_playlist.Count, at));
        return list;
    }

    /// <summary>How long items played one after another take, from the first starting to the last ending.</summary>
    double ChainLength(IReadOnlyList<Track> tracks) =>
        tracks.Take(tracks.Count - 1).Sum(Step) + tracks[^1].PlayLength;

    /// <summary>
    /// Where a backtimed event goes: before the upcoming item whose start is nearest to when it has to start (in
    /// <paramref name="left"/> seconds), but not inside another event's items nor before an earlier timed item.
    /// Late means fading the end of a song, so a gap a fill song can close counts for less than the same time late.
    /// -1 when there is nothing to go by.
    /// </summary>
    int BacktimePosition(double left, DateTime startAt)
    {
        if (Upcoming() is not { } upcoming) return -1;
        int best = -1;
        double bestError = double.MaxValue;
        foreach (var (index, at) in upcoming)
        {
            if (index < _playlist.Count && _playlist[index].StartBy is { } other && other > startAt) break; // keep timed items in order
            bool insideEvent = index > 0 && index < _playlist.Count && _playlist[index].Event != null && _playlist[index].Event == _playlist[index - 1].Event;
            if (insideEvent) continue;
            double error = at > left ? at - left
                : left - at >= BacktimeMinGap && _fill != null ? (left - at) / 4
                : left - at;
            if (error < bestError) { best = index; bestError = error; }
        }
        // Not before an earlier timed item.
        int lastTimed = -1;
        for (int i = _cursorIndex + 1; i < _playlist.Count; i++)
            if (_playlist[i].StartBy is { } t && t <= startAt && !_played.Contains(_playlist[i].Id)) lastTimed = i;
        if (lastTimed >= 0 && best <= lastTimed)
        {
            best = lastTimed + 1;
            while (best < _playlist.Count && _playlist[best].Event != null && _playlist[best].Event == _playlist[lastTimed].Event) best++;
        }
        return best;
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
        if (h == 0) { Emit(L.F("Time signal: {0}", Bass.LastError)); return; }

        // Mixed in with a delay so it is heard, after what is still queued on the card, exactly on time.
        double cartLatency = Bass.ChannelBytes2Seconds(_graph.CartBus, _graph.CartDelay);
        double startIn = Math.Max(0, untilHour - hourAt - cartLatency);
        if (!BassMix.MixerAddChannel(_graph.CartBus, h, BassFlags.AutoFree, Bass.ChannelSeconds2Bytes(_graph.CartBus, startIn), 0))
        {
            Emit(L.F("Time signal: {0}", Bass.LastError));
            Bass.StreamFree(h);
            return;
        }
        Emit(L.F("Time signal for {0:HH:mm}", hour));
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
        SetCurrent(null);
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
        if (Fill()) return; // Insert published the playlist
        var next = NextTrack();
        if (_preload is { } p && p.Id != next?.Id)
        {
            Bass.StreamFree(p.Handle);
            _preload = null;
        }
        if (_streamConnection is { } sc && sc.Id != next?.Id) DropConnection();
        if (next != null && !next.IsStream && _preload == null && _current != null && !_tagWriting.Contains(next.Path))
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
            Emit(L.F("Skipped {0}: {1}", Path.GetFileName(path), ex.Message));
            return null;
        }
    }

    Track? Probe(string path)
    {
        int h = OpenStream(path, prescan: false);
        if (h == 0)
        {
            Emit(L.F("Unsupported or unreadable: {0} ({1})", Path.GetFileName(path), Bass.LastError));
            return null;
        }
        try
        {
            double len = Bass.ChannelBytes2Seconds(h, Bass.ChannelGetLength(h, PositionFlags.Bytes));
            // TagLib, like the tag editor and the library: BASS's tag reader garbles UTF-16 ID3v2.3 text (what the
            // tag editor and many other programs write).
            TrackTags? tags = null;
            try { tags = Tags.Read(path); }
            catch (Exception ex)
            {
                // Malformed tags (e.g. a corrupt ID3v2 frame): fall back to the filename.
                Emit(L.F("Unreadable tags, using the filename: {0} ({1})", Path.GetFileName(path), ex.Message));
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

    internal static void LoadPlugins()
    {
        // FLAC needs bassflac everywhere. Windows and macOS decode AAC/M4A through the OS; Linux needs bass_aac.
        string[] files = OperatingSystem.IsWindows() ? ["bassflac.dll"]
            : OperatingSystem.IsMacOS() ? ["libbassflac.dylib"]
            : ["libbassflac.so", "libbass_aac.so"];
        foreach (var file in files)
        {
            string full = Path.Combine(AppContext.BaseDirectory, file);
            if (File.Exists(full)) Bass.PluginLoad(full);
        }
    }

    void Emit(string message) => Log?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}
