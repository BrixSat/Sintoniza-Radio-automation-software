using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Online's settings, this computer's own (online.json in the settings folder, readable only by this
/// computer's user: it holds the servers' passwords): what it streams, where to, how it keeps the stream up, alerts
/// and the lock.
/// </summary>
public sealed class OnlineSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>The station, as the window and the alerts call it.</summary>
    public string StationName { get; set; } = "";

    public RecSource Source { get; set; } = RecSource.Input;
    /// <summary>The input (the desk's programme output into the sound card); empty: the default input.</summary>
    public string Input { get; set; } = "";
    /// <summary>A stream relayed instead of an input.</summary>
    public string SourceUrl { get; set; } = "";

    public ServerSettings Server { get; set; } = new();
    /// <summary>The backup server, when <see cref="BackupEnabled"/>.</summary>
    public ServerSettings Backup { get; set; } = new();
    public bool BackupEnabled { get; set; }
    /// <summary>The main server failed this many times in a row: the backup is tried.</summary>
    public int BackupAfter { get; set; } = 2;

    public StreamFormat Format { get; set; } = StreamFormat.Mp3;
    public int Bitrate { get; set; } = 128;
    public bool Mono { get; set; }

    /// <summary>What the server and the players show: the stream's name, genre, website, description, and its title.</summary>
    public string StreamName { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Website { get; set; } = "";
    public string Description { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Listed in the server's public directory.</summary>
    public bool Public { get; set; }

    /// <summary>Seconds between attempts to connect.</summary>
    public int RetrySeconds { get; set; } = 5;

    /// <summary>Sent (looped) when the source is silent or stopped for <see cref="FallbackSeconds"/>; empty: silence.</summary>
    public string FallbackFile { get; set; } = "";
    public double FallbackSeconds { get; set; } = 30;

    /// <summary>The output it is heard on when monitoring is on, and whether it is.</summary>
    public string MonitorOutput { get; set; } = "";
    public bool Monitor { get; set; }

    /// <summary>Silence: below this level (dBFS) for this long is reported (and starts the fallback file's count).</summary>
    public double SilenceDb { get; set; } = -45;
    public double SilenceSeconds { get; set; } = 60;
    /// <summary>Off the air (not connected) this long: an alert.</summary>
    public int OfflineAlertSeconds { get; set; } = 30;
    public AlertSettings Alerts { get; set; } = new();
    /// <summary>"Still online" once a day at this hour (null: not sent).</summary>
    public int? AliveHour { get; set; } = 9;

    /// <summary>Goes online by itself when it opens.</summary>
    public bool StartOnline { get; set; } = true;
    /// <summary>A password asked for to stop the stream, change the settings or close (its hash; empty: no lock).</summary>
    public string LockHash { get; set; } = "";
    /// <summary>Unlocked, it locks again after this many minutes.</summary>
    public int LockMinutes { get; set; } = 5;

    public sealed class ServerSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 8000;
        public string Mount { get; set; } = "/live";
        public string User { get; set; } = "source";
        public string Password { get; set; } = "";
        public bool Shoutcast { get; set; }

        public OnlineServer ToServer() => new(Host.Trim(), Port, Mount.Trim(), User.Trim(), Password, Shoutcast);
    }

    public bool HasServer => Server.ToServer().IsSet;

    public OnlineOptions ToOptions() => new(
        Input,
        Source == RecSource.Stream && SourceUrl.Trim().Length > 0 ? SourceUrl.Trim() : null,
        Server.ToServer(),
        BackupEnabled && Backup.ToServer().IsSet ? Backup.ToServer() : null,
        Format, Bitrate, Mono,
        StreamName.Trim().Length > 0 ? StreamName : StationName, Genre, Website, Description, Public, Title,
        RetrySeconds, BackupAfter,
        FallbackFile.Trim().Length > 0 ? FallbackFile.Trim() : null, FallbackSeconds, SilenceDb);

    static string File => Path.Combine(AppSettings.DataFolder, "online.json");

    public static OnlineSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && JsonSerializer.Deserialize<OnlineSettings>(System.IO.File.ReadAllText(File), Json) is { } s) return s;
        }
        catch { /* unreadable: start from the defaults */ }
        return new OnlineSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        string tmp = File + ".tmp";
        // Only this computer's user can read it (Linux, macOS): the servers' passwords are in it. On Windows the
        // settings folder is the user's own already.
        if (OperatingSystem.IsWindows()) System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        else
        {
            System.IO.File.Delete(tmp); // a leftover one keeps its own permissions
            using (var stream = new FileStream(tmp, new FileStreamOptions
                   {
                       Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                   }))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonSerializer.Serialize(this, Json));
            System.IO.File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite); // also over an older file
        }
        System.IO.File.Move(tmp, File, overwrite: true);
    }

    public OnlineSettings Clone() => JsonSerializer.Deserialize<OnlineSettings>(JsonSerializer.Serialize(this, Json), Json)!;

    /// <summary>Whether a change needs the stream started again (what it sends, or where).</summary>
    public bool SameStream(OnlineSettings o) => JsonSerializer.Serialize(Stream(this), Json) == JsonSerializer.Serialize(Stream(o), Json);

    static object Stream(OnlineSettings s) => new
    {
        s.Source, s.Input, s.SourceUrl, s.Server, s.Backup, s.BackupEnabled, s.BackupAfter, s.Format, s.Bitrate, s.Mono, s.StreamName,
        s.StationName, s.Genre, s.Website, s.Description, s.Title, s.Public, s.RetrySeconds, s.FallbackFile, s.FallbackSeconds, s.SilenceDb,
    };

    public bool IsLocked => LockHash.Length > 0;
}
