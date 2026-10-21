using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The station's settings, the same in every studio: kept in the database (station_settings) and edited in Sintoniza
/// Pro. A playout's own Settings only has what belongs to that computer (sound cards, microphone, the database
/// login, keys…); these are put over its settings when it starts and whenever Pro saves them.
/// </summary>
public sealed class StationSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string StationName { get; set; } = "";
    public string StationGenre { get; set; } = "Various";
    public string StationUrl { get; set; } = "";
    public string CurrencySign { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StationType StationType { get; set; } = StationType.Fm;
    /// <summary>The logo (PNG or JPEG bytes, base64); null = none.</summary>
    public string? Logo { get; set; }

    public double Crossfade { get; set; } = 5;
    public double OtherCrossfade { get; set; }
    public PlaylistColors PlaylistColors { get; set; } = new();
    public int PlanDaysAhead { get; set; } = 2;

    public TimeSignalSettings TimeSignal { get; set; } = new();
    public LoudnessSettings Loudness { get; set; } = new();
    public SilenceSettings Silence { get; set; } = new();
    public bool FillEnabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FillSource FillSource { get; set; } = FillSource.Folder;
    public List<long> FillCategories { get; set; } = new();
    public double FillSongHours { get; set; } = 3;
    public int FillArtistMinutes { get; set; } = 60;
    public string FillFolder { get; set; } = "";

    /// <summary>What the RDS encoder is sent (how it is reached is each computer's).</summary>
    public string RdsPs { get; set; } = "";
    public string RdsTemplate { get; set; } = "{artist} - {title}";
    public string RdsDefaultText { get; set; } = "{station}";
    public bool RdsDefaultForJingles { get; set; } = true;
    public bool RdsRtPlus { get; set; }

    public WebNowPlayingSettings WebNowPlaying { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();

    public string StreamHost { get; set; } = "localhost";
    public int StreamPort { get; set; } = 8000;
    public string StreamMount { get; set; } = "/live";
    public string StreamPassword { get; set; } = "";
    public int StreamFormat { get; set; }
    public int StreamBitrateIndex { get; set; } = 2;
    public bool Shoutcast { get; set; }

    /// <summary>The cartwalls every studio has (the host picks one and fires its carts).</summary>
    public List<CartwallPage> Cartwalls { get; set; } = new();

    /// <summary>Folders the library scans besides the categories' own (their songs have no category until given one).</summary>
    public List<string> LibraryFolders { get; set; } = new();

    /// <summary>Where voice tracks are recorded to: a folder every studio can reach. Empty: asked the first time.</summary>
    public string VoiceTrackFolder { get; set; } = "";

    /// <summary>The programme service's name at the ERC (ALIAS in its file); empty: the station's name.</summary>
    public string ErcAlias { get; set; } = "";

    /// <summary>The countries songs' performers are from, with the ERC's code for each and whether it is in the EU.</summary>
    public List<ErcCountry> ErcCountries { get; set; } = Erc.DefaultCountries();

    /// <summary>This computer's settings as the station's (the first studio after the upgrade publishes its own).</summary>
    public static StationSettings From(AppSettings s)
    {
        string? logo = null;
        try { if (s.LogoPath is { } path && File.Exists(path)) logo = Convert.ToBase64String(File.ReadAllBytes(path)); }
        catch { /* no logo */ }
        return new StationSettings
        {
            StationName = s.StationName, StationGenre = s.StationGenre, StationUrl = s.StationUrl, CurrencySign = s.CurrencySign,
            StationType = s.StationType, Logo = logo,
            Crossfade = s.Crossfade, OtherCrossfade = s.OtherCrossfade, PlaylistColors = s.PlaylistColors, PlanDaysAhead = s.PlanDaysAhead,
            TimeSignal = s.TimeSignal, Loudness = s.Loudness, Silence = s.Silence,
            FillEnabled = s.FillEnabled, FillSource = s.FillSource, FillCategories = s.FillCategories.ToList(), FillSongHours = s.FillSongHours,
            FillArtistMinutes = s.FillArtistMinutes, FillFolder = s.FillFolder,
            RdsPs = s.Rds.Ps, RdsTemplate = s.Rds.Template, RdsDefaultText = s.Rds.DefaultText, RdsDefaultForJingles = s.Rds.DefaultForJingles,
            RdsRtPlus = s.Rds.RtPlus,
            WebNowPlaying = s.WebNowPlaying, Alerts = s.Alerts,
            StreamHost = s.StreamHost, StreamPort = s.StreamPort, StreamMount = s.StreamMount, StreamPassword = s.StreamPassword,
            StreamFormat = s.StreamFormat, StreamBitrateIndex = s.StreamBitrateIndex, Shoutcast = s.Shoutcast,
            Cartwalls = s.Cartwalls, LibraryFolders = s.Library.Folders.ToList(), VoiceTrackFolder = s.VoiceTrackFolder,
        };
    }

    /// <summary>Puts the station's settings over this computer's (in memory; the caller saves them as its copy).</summary>
    public void ApplyTo(AppSettings s)
    {
        s.StationName = StationName; s.StationGenre = StationGenre; s.StationUrl = StationUrl; s.CurrencySign = CurrencySign;
        s.StationType = StationType;
        s.LogoPath = SaveLogo();
        s.Crossfade = Crossfade; s.OtherCrossfade = OtherCrossfade; s.PlaylistColors = PlaylistColors ?? new(); s.PlanDaysAhead = PlanDaysAhead;
        s.TimeSignal = TimeSignal ?? new(); s.Loudness = Loudness ?? new(); s.Silence = Silence ?? new();
        s.FillEnabled = FillEnabled; s.FillSource = FillSource; s.FillCategories = FillCategories?.ToList() ?? new();
        s.FillSongHours = FillSongHours; s.FillArtistMinutes = FillArtistMinutes; s.FillFolder = FillFolder ?? "";
        s.Rds.Ps = RdsPs; s.Rds.Template = RdsTemplate; s.Rds.DefaultText = RdsDefaultText; s.Rds.DefaultForJingles = RdsDefaultForJingles;
        s.Rds.RtPlus = RdsRtPlus;
        s.WebNowPlaying = WebNowPlaying ?? new(); s.Alerts = Alerts ?? new();
        s.StreamHost = StreamHost; s.StreamPort = StreamPort; s.StreamMount = StreamMount; s.StreamPassword = StreamPassword;
        s.StreamFormat = StreamFormat; s.StreamBitrateIndex = StreamBitrateIndex; s.Shoutcast = Shoutcast;
        s.Cartwalls = Cartwalls is { Count: > 0 } pages ? pages : s.Cartwalls;
        if (s.ActiveCartwall is { } active && s.Cartwalls.All(c => c.Name != active)) s.ActiveCartwall = s.Cartwalls.FirstOrDefault()?.Name;
        s.Library.Folders = LibraryFolders?.ToList() ?? new();
        s.VoiceTrackFolder = VoiceTrackFolder ?? "";
    }

    /// <summary>The logo as a file of this computer (for the screen); null if there is none.</summary>
    string? SaveLogo()
    {
        if (string.IsNullOrEmpty(Logo)) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(Logo);
            string file = Path.Combine(AppSettings.DataFolder, "station-logo.png");
            if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(file, bytes);
            return file;
        }
        catch { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static StationSettings? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<StationSettings>(json, Json); }
        catch (JsonException) { return null; }
    }

    public StationSettings Clone() => FromJson(ToJson())!;
}

/// <summary>The station's settings in the database: read, saved as a new version (a save over a newer one is refused).</summary>
public sealed class StationStore(LibraryDb db)
{
    public const long Station = 1;

    public LibraryDb Db => db;

    /// <summary>The saved version (0 = never saved).</summary>
    public long Version()
    {
        using var c = db.Connect();
        return c.ExecuteScalar<long?>("SELECT version FROM station_settings WHERE station_id = @Station", new { Station }) ?? 0;
    }

    /// <summary>The settings and their version; null when they were never saved.</summary>
    public (StationSettings? Settings, long Version) Load()
    {
        using var c = db.Connect();
        var row = c.QueryFirstOrDefault<(string Settings, long Version)>("SELECT settings, version FROM station_settings WHERE station_id = @Station", new { Station });
        return row.Settings == null ? (null, 0) : (StationSettings.FromJson(row.Settings), row.Version);
    }

    /// <summary>
    /// Saves them as a new version. With <paramref name="expected"/>: only if nobody saved since that version was read
    /// (else <see cref="ScheduleStore.ChangedException"/>); with 0, only if they were never saved. Returns the new version.
    /// </summary>
    public long Save(StationSettings settings, long? expected, string by)
    {
        using var c = db.Connect();
        using var tx = c.BeginTransaction();
        var current = c.QueryFirstOrDefault<(long Version, string UpdatedAt, string UpdatedBy)?>(
            "SELECT version, updated_at, updated_by FROM station_settings WHERE station_id = @Station", new { Station }, tx);
        if (expected is { } want && (current?.Version ?? 0) != want)
            throw new ScheduleStore.ChangedException(
                DateTime.TryParse(current?.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToLocalTime() : DateTime.Now,
                current?.UpdatedBy ?? "");
        long version = (current?.Version ?? 0) + 1;
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        c.Execute(current == null
                ? "INSERT INTO station_settings (station_id, settings, version, updated_at, updated_by) VALUES (@Station, @json, @version, @now, @by)"
                : "UPDATE station_settings SET settings = @json, version = @version, updated_at = @now, updated_by = @by WHERE station_id = @Station",
            new { Station, json = settings.ToJson(), version, now, by }, tx);
        tx.Commit();
        return version;
    }
}
