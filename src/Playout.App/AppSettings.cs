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
    /// <summary>Top of the hour time signal: off, over what is playing, or instead of it.</summary>
    public TimeSignalSettings TimeSignal { get; set; } = new();
    /// <summary>Keyboard shortcuts by action id (see <see cref="global::Playout.App.Shortcuts"/>), as KeyGesture text; empty = none.</summary>
    /// <summary>Look for a new version on GitHub at start and every few hours.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public Dictionary<string, string> Shortcuts { get; set; } = global::Playout.App.Shortcuts.Defaults();
    public bool AutoAdvance { get; set; } = true;
    public List<PlaylistEntry> PlaylistItems { get; set; } = new();

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
    /// <summary>The player was playing in AUTO at the last save.</summary>
    public bool WasPlaying { get; set; }
    /// <summary>Streaming was switched on at the last save: it is switched on again at start.</summary>
    public bool StreamingOn { get; set; }

    /// <summary>Playlist from before streams could be added: plain file paths, moved into <see cref="PlaylistItems"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Playlist { get; set; }
    public string MusicFolder { get; set; } = DefaultMusicFolder();

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
        s.Normalize();
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
        Loudness ??= new();
        Library ??= new();
        Library.Folders ??= new();
        FillFolder ??= "";
        FillCategories ??= new();
        Shortcuts ??= new();
        // Actions added since the file was saved get their default.
        foreach (var (id, key) in global::Playout.App.Shortcuts.Defaults()) Shortcuts.TryAdd(id, key);
    }

    string? _saved; // what is in the file, so an unchanged save writes nothing

    /// <summary>Saves, if anything changed since the last save. Crash-safe: see <see cref="SafeFile"/>.</summary>
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

    [JsonIgnore]
    public bool IsStream => StreamName != null;

    public static PlaylistEntry From(Track t)
    {
        var e = t.IsStream ? new PlaylistEntry { Path = t.Path, StreamName = t.Title, StreamSeconds = t.Duration } : new PlaylistEntry { Path = t.Path };
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
    public ScheduledItem ToScheduled() => new(Path, Kind, StreamName, StreamSeconds, Advertiser, Lock, StartBy, Backtime);
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
