using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>Persisted UI state: carts, stream settings and the playlist (so a restart picks up where it left off).</summary>
public sealed class AppSettings
{
    public const int CartCount = 12;

    public string StationName { get; set; } = "";
    public string? LogoPath { get; set; }
    public string StationGenre { get; set; } = "Various";
    public string StationUrl { get; set; } = "";
    /// <summary>FM: the mic goes through the mixing desk. Web: the mic goes through this computer (MIC button).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StationType StationType { get; set; } = StationType.Fm;
    public MicSettings Mic { get; set; } = new();

    /// <summary>The microphone setup for the engine, or null when the mic isn't ours to handle (FM).</summary>
    [JsonIgnore]
    public MicSettings? ActiveMic => StationType == StationType.Web ? Mic ?? new() : null;
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
    /// <summary>Top of the hour time signal: off, over what is playing, or instead of it.</summary>
    public TimeSignalSettings TimeSignal { get; set; } = new();
    /// <summary>Keyboard shortcuts by action id (see <see cref="global::Playout.App.Shortcuts"/>), as KeyGesture text; empty = none.</summary>
    public Dictionary<string, string> Shortcuts { get; set; } = global::Playout.App.Shortcuts.Defaults();
    public bool AutoAdvance { get; set; } = true;
    public List<PlaylistEntry> PlaylistItems { get; set; } = new();

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

    /// <summary>Where the app keeps its files (settings, cue points).</summary>
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPlayout");

    static string FilePath => Path.Combine(DataFolder, "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
                s.Normalize();
                return s;
            }
        }
        catch
        {
            // A corrupt settings file must never stop the station from starting.
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
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
        Shortcuts ??= new();
        // Actions added since the file was saved get their default.
        foreach (var (id, key) in global::Playout.App.Shortcuts.Defaults()) Shortcuts.TryAdd(id, key);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Best effort.
        }
    }
}

public enum StationType { Fm, Web }

/// <summary>A saved playlist item: a file, or an internet stream (it has a <see cref="StreamName"/>) with its play time (0 = until Stop or Next).</summary>
public sealed class PlaylistEntry
{
    public string Path { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StreamName { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double StreamSeconds { get; set; }

    [JsonIgnore]
    public bool IsStream => StreamName != null;

    public static PlaylistEntry From(Track t) =>
        t.IsStream ? new() { Path = t.Path, StreamName = t.Title, StreamSeconds = t.Duration } : new() { Path = t.Path };
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
