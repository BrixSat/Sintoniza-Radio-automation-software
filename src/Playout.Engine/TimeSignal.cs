using System.Text.Json.Serialization;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// What happens at the top of the hour. Over: the time signal plays on top of whatever is on air.
/// Stop: what is on air fades out just before it, and the next playlist item starts right after it (in AUTO).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeSignalMode>))]
public enum TimeSignalMode { Off, Over, Stop }

/// <summary>Top of the hour time signal. File: an audio file timed to end exactly on the hour; empty = the built-in pips.</summary>
public sealed record TimeSignalSettings(TimeSignalMode Mode = TimeSignalMode.Off, string File = "");

/// <summary>The sound of the time signal, and where the hour falls in it.</summary>
static class TimeSignal
{
    const double PipHz = 1000;
    const double ShortPip = 0.1;
    const double LongPip = 0.5;
    const int ShortPips = 5; // one a second before the hour; the long one starts on the hour
    const float PipLevel = 0.35f; // about -9 dBFS

    /// <summary>Opens the signal as a decode stream (the pips if the file can't be played). HourAt: seconds into it where the hour is.</summary>
    public static int Open(string file, Action<string> log, out double hourAt, out double length)
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
