using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>Persisted UI state: carts, stream settings and the playlist (so a restart picks up where it left off).</summary>
public sealed class AppSettings
{
    public const int CartCount = 12;

    public string StationName { get; set; } = "";
    /// <summary>Interface language: "en", "pt", "fr"; "" = the computer's (Portuguese, French, else English). From the next start.</summary>
    public string Language { get; set; } = "";
    public string? LogoPath { get; set; }
    public string StationGenre { get; set; } = "Various";
    public string StationUrl { get; set; } = "";
    /// <summary>The money sign on advert rows and the break countdown; empty = this computer's currency.</summary>
    public string CurrencySign { get; set; } = "";
    /// <summary>FM: the mic goes through the mixing desk. Web: the mic goes through this computer (MIC button).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StationType StationType { get; set; } = StationType.Fm;
    public MicSettings Mic { get; set; } = new();
    /// <summary>The MIC button and key: false = hold to talk; true = press to open the mic, press again to close it.</summary>
    public bool MicToggle { get; set; }

    /// <summary>The microphone setup for the engine, or null when the mic isn't ours to handle (FM).</summary>
    [JsonIgnore]
    public MicSettings? ActiveMic => StationType == StationType.Web ? Mic ?? new() : null;
    /// <summary>Streaming is shown (STREAM button, stream time): web radio, or a server is set up (it has a source password).</summary>
    [JsonIgnore]
    public bool UsesStreaming => StationType == StationType.Web || !string.IsNullOrWhiteSpace(StreamPassword);
    /// <summary>Where the playlist (music) and the cartwall come out: sound card name (empty = default) + channel pair.</summary>
    public OutputRoute MusicOutput { get; set; } = new();
    public OutputRoute CartOutput { get; set; } = new();
    /// <summary>Pre-listen output for the cue editor; should be a pair that is not on air (e.g. the desk's PFL/cue input).</summary>
    public OutputRoute PreviewOutput { get; set; } = new();

    [JsonIgnore]
    public AudioRouting Routing => new(MusicOutput ?? new(), CartOutput ?? new(), PreviewOutput ?? new());

    /// <summary>Named sets of carts; the picker above the cartwall switches between them.</summary>
    public List<CartwallPage> Cartwalls { get; set; } = new();
    public string? ActiveCartwall { get; set; }

    /// <summary>Carts from before there were several cartwalls; moved into a "Default" cartwall on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string?[]? Carts { get; set; }
    public double Crossfade { get; set; } = 5;
    /// <summary>Crossfade into or out of jingles, adverts and programmes (song to song is <see cref="Crossfade"/>).</summary>
    public double OtherCrossfade { get; set; }
    /// <summary>The playlist's row backgrounds by type (Settings → Colours).</summary>
    public PlaylistColors PlaylistColors { get; set; } = new();
    /// <summary>The playlist shows this hour and the next (and the item on air); off: all of it.</summary>
    public bool PlaylistHourView { get; set; } = true;
    /// <summary>In that view, how much of the next hour shows (minutes, 0-60).</summary>
    public int PlaylistNextMinutes { get; set; } = 30;
    /// <summary>The daily playlists are kept made for today and this many days after (the playlist manager shows them).</summary>
    public int PlanDaysAhead { get; set; } = 2;
    /// <summary>The tab open beside the playlist: cartwall, library or folders.</summary>
    public string SideTab { get; set; } = "cartwall";
    /// <summary>The share of the window's width that panel takes (the rest is the playlist).</summary>
    public double SideShare { get; set; } = 0.4;
    /// <summary>The daily backup of the database and the station's files.</summary>
    public BackupSettings Backup { get; set; } = new();
    /// <summary>Alerts by Telegram, email or a web address when something goes wrong on air.</summary>
    public AlertSettings Alerts { get; set; } = new();
    /// <summary>GPIO through a serial port (this computer's: the desk is wired to it).</summary>
    public GpioSettings Gpio { get; set; } = new();
    /// <summary>What the on-air screen shows: the status lamps, big counters, the ON AIR sign (this computer's).</summary>
    public ScreenSettings Screen { get; set; } = new();
    /// <summary>Top of the hour time signal: off, over what is playing, or instead of it.</summary>
    public TimeSignalSettings TimeSignal { get; set; } = new();
    /// <summary>Keyboard shortcuts by action id (see <see cref="global::Playout.App.Shortcuts"/>), as KeyGesture text; empty = none.</summary>
    /// <summary>Look for a new version on GitHub at start and every few hours.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public Dictionary<string, string> Shortcuts { get; set; } = global::Playout.App.Shortcuts.Defaults();
    public bool AutoAdvance { get; set; } = true;
    /// <summary>ASSISTED because a live show was on: AUTO again when the app finds no show on (it was closed during it).</summary>
    public bool AssistedForShow { get; set; }
    public List<PlaylistEntry> PlaylistItems { get; set; } = new();

    /// <summary>
    /// The station this computer works for, in a library that can hold several (<see cref="CurrentStation"/>): its
    /// schedule, daily playlists, settings and play history. 1 until stations can be chosen.
    /// </summary>
    public long Station { get; set; } = 1;

    /// <summary>The music library database and the folders scanned into it (radio-import reads this too).</summary>
    public LibrarySettings Library { get; set; } = new();

    /// <summary>Now playing to the FM station's RDS encoder.</summary>
    public RdsSettings Rds { get; set; } = new();

    /// <summary>Now playing posted to the station's website.</summary>
    public WebNowPlayingSettings WebNowPlaying { get; set; } = new();

    /// <summary>Loudness levelling: every item played at the same perceived loudness.</summary>
    public LoudnessSettings Loudness { get; set; } = new();

    /// <summary>Dead air detection: what counts as silence and how long before AUTO skips.</summary>
    public SilenceSettings Silence { get; set; } = new();
    /// <summary>When the playlist runs out in AUTO, add music: from the library (<see cref="FillSource"/>) or <see cref="FillFolder"/>.</summary>
    public bool FillEnabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FillSource FillSource { get; set; } = FillSource.Folder;
    /// <summary>Library categories fill music picks from (ids); empty = every music category.</summary>
    public List<long> FillCategories { get; set; } = new();
    /// <summary>Fill music from the library doesn't play a song again within this many hours…</summary>
    public double FillSongHours { get; set; } = 3;
    /// <summary>…or the same artist within this many minutes, if it has other choices.</summary>
    public int FillArtistMinutes { get; set; } = 60;
    /// <summary>Fill music from a folder; with the library, used while the library can't be reached or has nothing to pick.</summary>
    public string FillFolder { get; set; } = "";
    /// <summary>When the app starts and AUTO was playing when it closed, crashed or the computer went down: play on.</summary>
    public bool ResumeOnStart { get; set; } = true;
    /// <summary>Run under a watchdog that starts the app again if it crashes or its audio engine hangs.</summary>
    public bool RestartAfterCrash { get; set; } = true;

    /// <summary>Index in <see cref="PlaylistItems"/> of the item on air (or last played); -1 = none. Saved every few seconds.</summary>
    public int OnAirIndex { get; set; } = -1;
    /// <summary>
    /// When the on-air program was last running, as found at start (it marks it every 30 seconds, also while one long
    /// item plays and nothing in the settings changes); null: never marked.
    /// </summary>
    [JsonIgnore]
    public DateTime? SavedAtUtc { get; private set; }

    static string RunningFile => Path.Combine(DataFolder, "running");

    /// <summary>The on-air program is running (every 30 seconds).</summary>
    public static void MarkRunning()
    {
        try
        {
            if (!File.Exists(RunningFile)) File.WriteAllText(RunningFile, "");
            File.SetLastWriteTimeUtc(RunningFile, DateTime.UtcNow);
        }
        catch
        {
            // Best effort: without it the settings' own time is used.
        }
    }
    /// <summary>The player was playing in AUTO at the last save.</summary>
    public bool WasPlaying { get; set; }
    /// <summary>
    /// This computer was standby (another was on air) when last seen: at start it stays silent until the database says
    /// which computer is on air.
    /// </summary>
    public bool WasStandby { get; set; }
    /// <summary>Standby: goes on air by itself when the computer on air stops answering for <see cref="TakeOverSeconds"/>.</summary>
    public bool TakeOver { get; set; } = true;
    public int TakeOverSeconds { get; set; } = 15;
    /// <summary>Streaming was switched on at the last save: it is switched on again at start.</summary>
    public bool StreamingOn { get; set; }

    /// <summary>Playlist from before streams could be added: plain file paths, moved into <see cref="PlaylistItems"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Playlist { get; set; }
    public string MusicFolder { get; set; } = DefaultMusicFolder();
    /// <summary>The last Sintoniza user who logged in on this computer (filled in at the next login).</summary>
    public string LastUser { get; set; } = "";
    /// <summary>Where Sintoniza Rec's recordings are, for Pro's certificates (null: Sintoniza Rec's folder on this computer).</summary>
    public string? RecordingsFolder { get; set; }
    /// <summary>The web remote (this computer's: the server runs on it).</summary>
    public RemoteSettings Remote { get; set; } = new();
    /// <summary>Where voice tracks are kept (the station's; a folder every studio can reach). Empty: not chosen yet.</summary>
    public string VoiceTrackFolder { get; set; } = "";

    /// <summary>The station's takes (the time, the temperature): their folder and where the station is.</summary>
    public TakesSettings Takes { get; set; } = new();

    /// <summary>A gap before a break that ends at its time up to this long is filled from the Fillers category.</summary>
    public double FillerGapSeconds { get; set; } = 120;

    public string StreamHost { get; set; } = "localhost";
    public int StreamPort { get; set; } = 8000;
    public string StreamMount { get; set; } = "/live";
    public string StreamPassword { get; set; } = "";
    public int StreamFormat { get; set; }
    public int StreamBitrateIndex { get; set; } = 2;
    public bool Shoutcast { get; set; }

    static string DefaultMusicFolder()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Directory.Exists(music) ? music : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Where the app keeps its files (settings, cue points, logs). The system folder is created if missing: otherwise
    /// .NET returns "" and the files would land in whatever folder the app was started from.
    /// </summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "RadioPlayout");

    static string FilePath => Path.Combine(DataFolder, "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        // A damaged settings file must never stop the station from starting: then the backup, or the defaults.
        var s = SafeFile.Read(FilePath, json => JsonSerializer.Deserialize<AppSettings>(json)) ?? new();
        s.SavedAtUtc = File.Exists(RunningFile) ? File.GetLastWriteTimeUtc(RunningFile) : File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : null;
        s.Normalize();
        CurrentStation.Id = s.Station; // every program on this computer works for this station
        s._saved = JsonSerializer.Serialize(s, Json);
        return s;
    }

    /// <summary>Moves old-style carts into a cartwall and makes sure there is at least one, each with <see cref="CartCount"/> carts.</summary>
    void Normalize()
    {
        Cartwalls ??= new();
        if (Cartwalls.Count == 0)
            Cartwalls.Add(new CartwallPage { Name = "Default", Carts = (Carts ?? []).Select(p => new CartItem { Path = p }).ToList() });
        Carts = null;
        foreach (var page in Cartwalls)
        {
            page.Carts ??= new();
            for (int i = 0; i < page.Carts.Count; i++) page.Carts[i] ??= new CartItem();
            while (page.Carts.Count < CartCount) page.Carts.Add(new CartItem());
            if (page.Carts.Count > CartCount) page.Carts.RemoveRange(CartCount, page.Carts.Count - CartCount);
        }
        if (Cartwalls.All(c => c.Name != ActiveCartwall)) ActiveCartwall = Cartwalls[0].Name;

        PlaylistItems ??= new();
        if (Playlist != null && PlaylistItems.Count == 0)
            PlaylistItems = Playlist.Select(p => new PlaylistEntry { Path = p }).ToList();
        Playlist = null;
        TimeSignal ??= new();
        Silence ??= new();
        Rds ??= new();
        WebNowPlaying ??= new();
        Screen ??= new();
        Screen.HiddenLamps ??= new();
        Loudness ??= new();
        Library ??= new();
        Library.Folders ??= new();
        Remote ??= new();
        Library.FolderCategories ??= new();
        FillFolder ??= "";
        FillCategories ??= new();
        PlaylistColors ??= new();
        Backup ??= new();
        Alerts ??= new();
        Shortcuts ??= new();
        // Actions added since the file was saved get their default.
        foreach (var (id, key) in global::Playout.App.Shortcuts.Defaults()) Shortcuts.TryAdd(id, key);
    }

    string? _saved; // what is in the file, so an unchanged save writes nothing

    /// <summary>Saves, if anything changed since the last save. Crash-safe: see <see cref="SafeFile"/>.</summary>
    /// <summary>
    /// For Sintoniza Pro, Rec and Import, which share this computer's settings with the on-air program: the file is read
    /// afresh, only <paramref name="change"/> is made to it and it is saved, so what the on-air program saved meanwhile
    /// (its playlist and where it was) is never put back to an older copy.
    /// </summary>
    public static void Change(Action<AppSettings> change)
    {
        var fresh = Load();
        change(fresh);
        fresh.Save();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, Json);
            if (json == _saved) return;
            SafeFile.WriteAllText(FilePath, json);
            _saved = json;
        }
        catch
        {
            // Best effort; the next save tries again.
        }
    }
}

public enum StationType { Fm, Web }

public enum FillSource { Folder, Library }

/// <summary>A saved playlist item: a file, or an internet stream (it has a <see cref="StreamName"/>) with its play time (0 = until Stop or Next).</summary>
public sealed class PlaylistEntry
{
    public string Path { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StreamName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double StreamSeconds { get; set; }

    /// <summary>Put there by a scheduled event or ad break (its label), with the item's type and advertiser.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Advertiser { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TrackKind? Kind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemLock Lock { get; set; }
    /// <summary>The latest it may start (UTC), and whether the playlist before it is backtimed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? StartBy { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Backtime { get; set; }
    /// <summary>When it went on air (UTC), and whether it is part of an ad break.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? PlayedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool InBreak { get; set; }
    /// <summary>The planned hour it came from, and whether it may be dropped to keep time (see <see cref="PlanTag"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; set; }
    /// <summary>A voice track: how far it starts over the item before, and that it belongs to it.</summary>
    public double Overlap { get; set; }
    public bool FollowsPrevious { get; set; }
    /// <summary>The presenter's note from the daily playlists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }

    [JsonIgnore]
    public bool IsStream => StreamName != null;

    /// <summary>Restored as it was (the engine's type, when it went on air), not opened as a plain file.</summary>
    [JsonIgnore]
    public bool HasDetails => PlayedAt != null || Kind != null || Tag != null || Note != null;

    public static PlaylistEntry From(Track t)
    {
        var e = t.IsStream ? new PlaylistEntry { Path = t.Path, StreamName = t.Title, StreamSeconds = t.Duration } : new PlaylistEntry { Path = t.Path };
        e.PlayedAt = t.PlayedAt;
        e.InBreak = t.InBreak;
        e.Tag = t.Tag;
        e.Overlap = t.Overlap;
        e.FollowsPrevious = t.FollowsPrevious;
        e.Note = t.Note;
        // A jingle from a smart playlist's jingle category stays a jingle wherever the file is.
        if (t.Kind is not (TrackKind.Music or TrackKind.Stream)) e.Kind = t.Kind;
        if (t.Event != null)
        {
            e.Event = t.Event;
            e.Advertiser = t.Advertiser;
            e.Kind = t.Kind;
            e.Lock = t.Lock;
            e.StartBy = t.StartBy;
            e.Backtime = t.Backtime;
        }
        return e;
    }

    /// <summary>A scheduled item, as the engine takes it back.</summary>
    public ScheduledItem ToScheduled() => new(Path, Kind, StreamName, StreamSeconds, Advertiser, Lock, StartBy, Backtime, PlayedAt, InBreak, Tag, Overlap, FollowsPrevious, Note);
}

/// <summary>A named set of <see cref="AppSettings.CartCount"/> carts.</summary>
public sealed class CartwallPage
{
    public string Name { get; set; } = "";
    public List<CartItem> Carts { get; set; } = new();
}

/// <summary>One cart button: its file, and optionally a colour (hex) and an icon (a <see cref="CartLook"/> key). Colour and icon stay when the file changes.</summary>
public sealed class CartItem
{
    public string? Path { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }
}
