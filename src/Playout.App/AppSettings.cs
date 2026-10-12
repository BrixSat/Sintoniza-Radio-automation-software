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
    /// <summary>Where the playlist (music) and the cartwall come out: sound card name (empty = default) + channel pair.</summary>
    public OutputRoute MusicOutput { get; set; } = new();
    public OutputRoute CartOutput { get; set; } = new();

    [JsonIgnore]
    public AudioRouting Routing => new(MusicOutput ?? new(), CartOutput ?? new());

    public string?[] Carts { get; set; } = new string?[CartCount];
    public double Crossfade { get; set; } = 5;
    public bool AutoAdvance { get; set; } = true;
    public List<string> Playlist { get; set; } = new();
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

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPlayout", "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
                var carts = s.Carts ?? new string?[CartCount];
                Array.Resize(ref carts, CartCount);
                s.Carts = carts;
                return s;
            }
        }
        catch
        {
            // A corrupt settings file must never stop the station from starting.
        }
        return new AppSettings();
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
