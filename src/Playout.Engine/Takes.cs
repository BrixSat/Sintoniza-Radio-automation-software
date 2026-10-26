using System.Text.RegularExpressions;

namespace Playout.Engine;

/// <summary>What a take says: the time, the temperature, or the time and then the temperature.</summary>
public enum TakeKind { Time, Temperature, TimeAndTemperature }

/// <summary>
/// Takes: short recorded inserts the playout puts together when they go on air, so they are always right — the time
/// (HRS10 + MIN15: "ten… fifteen") and the temperature (TMP18, TMPM03 for −3). In the playlist a take is a placeholder
/// (its path is "take:time", "take:temperature" or "take:time+temperature"); the files are chosen the moment it starts.
/// </summary>
public static class Takes
{
    public const string Prefix = "take:";

    public static string PathFor(TakeKind kind) => Prefix + kind switch
    {
        TakeKind.Time => "time",
        TakeKind.Temperature => "temperature",
        _ => "time+temperature",
    };

    public static bool IsTake(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

    public static TakeKind? KindOf(string path) => path switch
    {
        Prefix + "time" => TakeKind.Time,
        Prefix + "temperature" => TakeKind.Temperature,
        Prefix + "time+temperature" => TakeKind.TimeAndTemperature,
        _ => null,
    };

    /// <summary>About how long one plays (for the playlist's times until it is put together).</summary>
    public static double Estimate(TakeKind kind) => kind == TakeKind.TimeAndTemperature ? 5 : 3;

    static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".mp2", ".aiff", ".aif"];

    /// <summary>
    /// The files for a take at <paramref name="at"/> (local time), in order; empty if a file it needs isn't there.
    /// The time is the nearest minute: HRS{hour} (midnight 00 or 24) and MIN{minute} (none needed on the hour), or one
    /// file HRS{hour}_{minute} if there is one. The temperature: TMP{degrees} or TMPM{degrees} below zero.
    /// </summary>
    public static List<string> Files(string folder, TakeKind kind, DateTime at, int? temperature)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return files;
        string[] all;
        try { all = Directory.GetFiles(folder).Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray(); }
        catch { return files; }
        if (kind is TakeKind.Time or TakeKind.TimeAndTemperature)
        {
            var t = at.AddSeconds(30);
            int hour = t.Hour, minute = t.Minute;
            if (Pick(all, "HRS", hour, minute) is { } both) files.Add(both);
            else
            {
                var h = Pick(all, "HRS", hour) ?? (hour == 0 ? Pick(all, "HRS", 24) : null);
                var m = minute == 0 ? null : Pick(all, "MIN", minute);
                if (h != null && (minute == 0 || m != null))
                {
                    files.Add(h);
                    if (m != null) files.Add(m);
                    else if (Pick(all, "MIN", 0) is { } zero) files.Add(zero); // "…o'clock", if there is one
                }
                else if (kind == TakeKind.Time) return [];
                // The time and temperature without the time's files: the temperature alone.
            }
        }
        if (kind is TakeKind.Temperature or TakeKind.TimeAndTemperature)
        {
            if (temperature is not { } degrees || (degrees < 0 ? Pick(all, "TMPM", -degrees) : Pick(all, "TMP", degrees)) is not { } file)
                return kind == TakeKind.TimeAndTemperature ? files : []; // the time alone, without a temperature
            files.Add(file);
        }
        return files;
    }

    /// <summary>Why a take has no files now, in words that say which file to add (null: it has them).</summary>
    public static string? Missing(string folder, TakeKind kind, DateTime at, int? temperature)
    {
        if (string.IsNullOrWhiteSpace(folder)) return L.T("no takes folder (Station settings → Takes)");
        if (!Directory.Exists(folder)) return L.F("the takes folder {0} isn't there", folder);
        if (Files(folder, kind, at, temperature).Count > 0) return null;
        var t = at.AddSeconds(30);
        string time = t.Minute == 0 ? $"HRS{t.Hour:00}" : $"HRS{t.Hour:00} + MIN{t.Minute:00}";
        string? degrees = temperature is { } d ? (d < 0 ? $"TMPM{-d:00}" : $"TMP{d:00}") : null;
        return kind switch
        {
            TakeKind.Time => L.F("no {0} file in the takes folder", time),
            _ when degrees == null => kind == TakeKind.Temperature
                ? L.T("the temperature isn't known (Station settings → Takes: where the station is; it needs the internet)")
                : L.F("no {0} file in the takes folder, and the temperature isn't known", time),
            TakeKind.Temperature => L.F("no {0} file in the takes folder", degrees),
            _ => L.F("no {0} file nor {1} in the takes folder", time, degrees),
        };
    }

    static readonly Regex TakeName = new(@"^(TMPM?|HRS|MIN)[ _-]?\d", RegexOptions.IgnoreCase);

    /// <summary>
    /// These files are takes (TMP18, HRS10, MIN15…), most of them: a line that picks one at random from them plays a
    /// temperature or a time at random — the time or temperature now needs a take line.
    /// </summary>
    public static bool AreTakeFiles(IReadOnlyCollection<string> paths) =>
        paths.Count >= 3 && paths.Count(p => TakeName.IsMatch(Path.GetFileNameWithoutExtension(p))) * 2 >= paths.Count;

    /// <summary>A file named PREFIX + number (+ _number), any case, leading zeros or a letter after it (several takes: one at random).</summary>
    static string? Pick(string[] files, string prefix, int number, int? second = null)
    {
        var pattern = second == null
            ? new Regex($"^{prefix}[ _-]?0*{number}(?![0-9])[^0-9]*$", RegexOptions.IgnoreCase)
            : new Regex($"^{prefix}[ _-]?0*{number}[ _-]0*{second}(?![0-9])[^0-9]*$", RegexOptions.IgnoreCase);
        // TMP3 isn't TMPM3: the letter after the prefix must not be another letter of a longer prefix.
        var matches = files.Where(f => pattern.IsMatch(Path.GetFileNameWithoutExtension(f))).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return matches.Count == 0 ? null : matches[Random.Shared.Next(matches.Count)];
    }
}
