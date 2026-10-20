using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// Amplitude envelope of a file: per-slice peak and RMS (0..1, both channels combined),
/// at <see cref="PointsPerSecond"/> slices per second.
/// </summary>
public sealed record Waveform(float[] Peak, float[] Rms, double PointsPerSecond)
{
    public double Duration => Peak.Length / PointsPerSecond;
}

/// <summary>Builds waveforms by decoding files on a background thread (never on the on-air path), with a small cache.</summary>
public sealed class WaveformLoader
{
    public const int PointsPerSecond = 50;
    const int CacheSize = 16;

    readonly Dictionary<string, Task<Waveform?>> _cache = new();
    readonly LinkedList<string> _order = new();
    readonly object _lock = new();

    public Task<Waveform?> LoadAsync(string path)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var task = Task.Run(() => Build(path));
            _cache[path] = task;
            _order.AddLast(path);
            if (_order.Count > CacheSize)
            {
                _cache.Remove(_order.First!.Value);
                _order.RemoveFirst();
            }
            return task;
        }
    }

    static Waveform? Build(string path)
    {
        int h = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (h == 0) return null;
        try
        {
            var info = Bass.ChannelGetInfo(h);
            int channels = Math.Max(1, info.Channels);
            int framesPerPoint = Math.Max(1, info.Frequency / PointsPerSecond);
            int samplesPerPoint = framesPerPoint * channels;

            var peaks = new List<float>(PointsPerSecond * 300);
            var rms = new List<float>(PointsPerSecond * 300);
            var buffer = new float[samplesPerPoint * 64];

            float peak = 0;
            double sumSquares = 0;
            int inPoint = 0;

            while (true)
            {
                int bytes = Bass.ChannelGetData(h, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0) break;
                int count = bytes / sizeof(float);
                for (int i = 0; i < count; i++)
                {
                    float v = Math.Abs(buffer[i]);
                    if (v > peak) peak = v;
                    sumSquares += v * v;
                    if (++inPoint == samplesPerPoint)
                    {
                        peaks.Add(Math.Min(1f, peak));
                        rms.Add((float)Math.Min(1, Math.Sqrt(sumSquares / samplesPerPoint)));
                        peak = 0;
                        sumSquares = 0;
                        inPoint = 0;
                    }
                }
            }
            if (inPoint > 0)
            {
                peaks.Add(Math.Min(1f, peak));
                rms.Add((float)Math.Min(1, Math.Sqrt(sumSquares / inPoint)));
            }
            return new Waveform(peaks.ToArray(), rms.ToArray(), PointsPerSecond);
        }
        finally
        {
            Bass.StreamFree(h);
        }
    }
}
