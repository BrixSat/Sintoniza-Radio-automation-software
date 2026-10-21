using System.Text.Json;
using System.Text.Json.Serialization;
using Playout.Engine;

namespace Playout.App;

/// <summary>Where Sintoniza Rec records from.</summary>
public enum RecSource { Input, Stream }

/// <summary>
/// Sintoniza Rec's settings, this computer's own (rec.json in the settings folder): where it records from, how and
/// where the files go and for how long they are kept, the archive copy, monitoring and alerts.
/// </summary>
public sealed class RecSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>The station, as the files and alerts call it.</summary>
    public string StationName { get; set; } = "";

    public RecSource Source { get; set; } = RecSource.Input;
    /// <summary>The input (the desk's programme output into the sound card); empty: the default input.</summary>
    public string Input { get; set; } = "";
    /// <summary>The station's stream, when recording that instead.</summary>
    public string StreamUrl { get; set; } = "";

    /// <summary>The recordings: a folder a day inside it.</summary>
    public string Folder { get; set; } = DefaultFolder;
    /// <summary>Put before the date in each file's name (e.g. RE_2026-10-21_10-00-00.mp3).</summary>
    public string Prefix { get; set; } = "";
    public RecFormat Format { get; set; } = RecFormat.Mp3;
    public int Bitrate { get; set; } = 128;
    public bool Mono { get; set; }
    /// <summary>A new file every so many minutes (1–60)…</summary>
    public int FileMinutes { get; set; } = 60;
    /// <summary>…starting at this minute of the hour (0: on the hour).</summary>
    public int StartMinute { get; set; }
    /// <summary>Days the recordings are kept; older ones are deleted (the law asks for at least 30).</summary>
    public int KeepDays { get; set; } = 45;

    /// <summary>A second, small copy kept much longer (an archive of the station's sound).</summary>
    public bool Archive { get; set; }
    public string ArchiveFolder { get; set; } = "";
    public int ArchiveBitrate { get; set; } = 32;
    /// <summary>Months the archive is kept (0: always).</summary>
    public int ArchiveKeepMonths { get; set; }

    /// <summary>The output it is heard on when monitoring is on, and whether it is.</summary>
    public string MonitorOutput { get; set; } = "";
    public bool Monitor { get; set; }

    /// <summary>Silence: below this level (dBFS) for this long is reported.</summary>
    public double SilenceDb { get; set; } = -45;
    public double SilenceSeconds { get; set; } = 60;
    /// <summary>Where alerts go (silence, a recording that can't be written, the disk filling up), as in the playout.</summary>
    public AlertSettings Alerts { get; set; } = new();
    /// <summary>"Still recording" once a day at this hour (null: not sent): no message one day means something is wrong.</summary>
    public int? AliveHour { get; set; } = 9;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic, Environment.SpecialFolderOption.Create) is { Length: > 0 } music
        ? music : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Sintoniza Rec");

    static string File => Path.Combine(AppSettings.DataFolder, "rec.json");

    public static RecSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(File) && JsonSerializer.Deserialize<RecSettings>(System.IO.File.ReadAllText(File), Json) is { } s) return s;
        }
        catch { /* unreadable: start from the defaults */ }
        return new RecSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        string tmp = File + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        System.IO.File.Move(tmp, File, overwrite: true);
    }

    public RecSettings Clone() => JsonSerializer.Deserialize<RecSettings>(JsonSerializer.Serialize(this, Json), Json)!;

    public string Extension => Format == RecFormat.Wav ? ".wav" : ".mp3";
}
