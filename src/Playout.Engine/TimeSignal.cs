using System.Text.Json.Serialization;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// What happens at the top of the hour. Over: the time signal plays on top of whatever is on air.
/// Stop: what is on air fades out just before it, and the next playlist item starts right after it (in AUTO).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeSignalMode>))]
public enum TimeSignalMode { Off, Over, Stop }

/// <summary>Where an hour announcement (HRS05.mp3 at 05:00) goes: instead of the time signal, ending on the hour, or right after it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HourAnnouncement>))]
public enum HourAnnouncement { Instead, After }

/// <summary>
/// Top of the hour time signal. File: an audio file timed to end exactly on the hour; empty = the built-in pips.
/// HourFolder: a file for each hour, its number at the end of the name (HRS05.mp3 at 05:00); empty = none.
/// </summary>
public sealed record TimeSignalSettings(TimeSignalMode Mode = TimeSignalMode.Off, string File = "", string HourFolder = "",
    HourAnnouncement HourAt = HourAnnouncement.Instead);

/// <summary>The sound of the time signal, and where the hour falls in it.</summary>
public static class TimeSignal
{
    const double PipHz = 1000;
    const double ShortPip = 0.1;
    const double LongPip = 0.5;
    const int ShortPips = 5; // one a second before the hour; the long one starts on the hour
    const float PipLevel = 0.35f; // about -9 dBFS

    static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".mp2", ".aiff", ".aif"];

    /// <summary>
    /// The announcement for the hour that starts at <paramref name="hour"/>: the audio file in the folder whose name
    /// ends in that number (HRS05.mp3, "05 horas.wav"; midnight 00 or 24), an HRS file first. Never another take of the
    /// same folder — a temperature (TMP12, TMPM03), a minute (MIN12) or an hour and minute (HRS10_12) — so a folder
    /// with all the takes works too. Null if there is none.
    /// </summary>
    public static string? HourFile(string folder, DateTime hour)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        try
        {
            string? any = null;
            foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!AudioExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                if (NotAnHour.IsMatch(name)) continue;
                int end = name.Length;
                while (end > 0 && !char.IsAsciiDigit(name[end - 1])) end--;
                int start = end;
                while (start > 0 && char.IsAsciiDigit(name[start - 1])) start--;
                if (start == end || !int.TryParse(name[start..end], out int n)) continue;
                if (n != hour.Hour && !(hour.Hour == 0 && n == 24)) continue;
                if (name.StartsWith("HRS", StringComparison.OrdinalIgnoreCase)) return file;
                any ??= file;
            }
            return any;
        }
        catch (Exception) { /* folder unreadable: no announcement */ }
        return null;
    }

    /// <summary>Takes that aren't an hour: TMP12, TMPM03, MIN15, HRS10_15.</summary>
    static readonly System.Text.RegularExpressions.Regex NotAnHour =
        new(@"^(TMP|MIN)|^HRS[ _-]?\d+[ _-]\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Opens the signal as a decode stream (the pips if the file can't be played). HourAt: seconds into it where the hour is.</summary>
    internal static int Open(string file, Action<string> log, out double hourAt, out double length)
    {
        if (!string.IsNullOrEmpty(file))
        {
            int h = Bass.CreateStream(file, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (h != 0)
            {
                length = hourAt = Bass.ChannelBytes2Seconds(h, Bass.ChannelGetLength(h, PositionFlags.Bytes));
                return h;
            }
            log(L.F("Time signal: cannot play {0} ({1}); using the pips", Path.GetFileName(file), Bass.LastError));
        }
        hourAt = ShortPips;
        length = ShortPips + LongPip;
        return Pips();
    }

    /// <summary>Five short pips, one a second, and a long one starting on the hour (like the Greenwich time signal).</summary>
    static int Pips()
    {
        int rate = AudioGraph.SampleRate;
        int frames = (int)((ShortPips + LongPip) * rate);
        var data = new float[frames * 2];
        for (int pip = 0; pip <= ShortPips; pip++)
        {
            int start = pip * rate;
            int count = (int)((pip == ShortPips ? LongPip : ShortPip) * rate);
            int ramp = rate / 200; // 5 ms in and out, no clicks
            for (int i = 0; i < count; i++)
            {
                double env = Math.Min(1, Math.Min(i, count - i) / (double)ramp);
                float v = (float)(PipLevel * env * Math.Sin(2 * Math.PI * PipHz * i / rate));
                data[(start + i) * 2] = v;
                data[(start + i) * 2 + 1] = v;
            }
        }
        int h = Bass.CreateStream(rate, 2, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        if (h != 0) Bass.StreamPutData(h, data, (data.Length * sizeof(float)) | unchecked((int)0x80000000)); // last block: end of stream
        return h;
    }
}
