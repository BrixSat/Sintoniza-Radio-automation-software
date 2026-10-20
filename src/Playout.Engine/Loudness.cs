using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// Loudness levelling: every item is played at the same perceived loudness. <paramref name="TargetLufs"/> is the
/// loudness items are brought to; quiet ones are raised by at most <paramref name="MaxBoostDb"/>, and never so far
/// that their peaks go over -1 dBFS.
/// </summary>
public sealed record LoudnessSettings(bool Enabled = false, double TargetLufs = -16, double MaxBoostDb = 6);

/// <summary>A file's integrated loudness (LUFS, EBU R128 / ITU-R BS.1770) and its sample peak (dBFS).</summary>
public readonly record struct LoudnessInfo(double Lufs, double PeakDb);

/// <summary>Measures integrated loudness as EBU R128 does: K-weighting, 400 ms blocks every 100 ms, gated at -70 LUFS and 10 LU under the average.</summary>
public static class LoudnessMeter
{
    const double AbsoluteGate = -70;
    const double RelativeGate = -10;

    /// <summary>Decodes the whole file (a few hundred milliseconds for a song); null if it can't be read or has no sound.</summary>
    public static LoudnessInfo? Measure(string path, CancellationToken cancel = default)
    {
        int h = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (h == 0) return null;
        try
        {
            var info = Bass.ChannelGetInfo(h);
            int channels = Math.Max(1, info.Channels);
            int rate = info.Frequency;
            var filters = Enumerable.Range(0, channels).Select(_ => new KWeighting(rate)).ToArray();
            // BS.1770 channel weights: 1 for left, right and centre; for 5.1, none for the LFE and 1.41 for the surrounds.
            double[] weights = channels == 6 ? [1, 1, 1, 0, 1.41, 1.41] : Enumerable.Repeat(1.0, channels).ToArray();

            int segmentFrames = rate / 10; // 100 ms
            var segments = new List<double>(); // weighted sum of squares per 100 ms
            double sum = 0, peak = 0;
            int frames = 0;
            var buffer = new float[16384 - 16384 % channels];
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                int got = Bass.ChannelGetData(h, buffer, (buffer.Length * sizeof(float)) | (int)DataFlags.Float);
                if (got <= 0) break;
                int samples = got / sizeof(float);
                for (int i = 0; i + channels <= samples; i += channels)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        double x = buffer[i + c];
                        double a = Math.Abs(x);
                        if (a > peak) peak = a;
                        double y = filters[c].Process(x);
                        sum += weights[c] * y * y;
                    }
                    if (++frames == segmentFrames)
                    {
                        segments.Add(sum);
                        sum = 0;
                        frames = 0;
                    }
                }
            }

            // 400 ms blocks overlapping by 75%: four segments each.
            var blocks = new List<double>();
            for (int i = 0; i + 4 <= segments.Count; i++)
                blocks.Add((segments[i] + segments[i + 1] + segments[i + 2] + segments[i + 3]) / (4.0 * segmentFrames));
            var loud = blocks.Where(z => Lufs(z) > AbsoluteGate).ToList();
            if (loud.Count == 0) return null;
            double gate = Lufs(loud.Average()) + RelativeGate;
            var gated = loud.Where(z => Lufs(z) > gate).ToList();
            if (gated.Count == 0) return null;
            return new LoudnessInfo(Math.Round(Lufs(gated.Average()), 2), Math.Round(peak > 0 ? 20 * Math.Log10(peak) : -120, 2));
        }
        finally
        {
            Bass.StreamFree(h);
        }
    }

    static double Lufs(double meanSquare) => meanSquare > 0 ? -0.691 + 10 * Math.Log10(meanSquare) : double.NegativeInfinity;

    /// <summary>The BS.1770 K-weighting: a high shelf (the head) then a high pass, for any sample rate.</summary>
    sealed class KWeighting
    {
        readonly double b0, b1, b2, a1, a2;      // shelf
        readonly double c0, c1, c2, d1, d2;      // high pass
        double x1, x2, y1, y2, u1, u2, v1, v2;

        public KWeighting(int rate)
        {
            double f0 = 1681.974450955533, g = 3.999843853973347, q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / rate);
            double vh = Math.Pow(10, g / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            b0 = (vh + vb * k / q + k * k) / a0;
            b1 = 2 * (k * k - vh) / a0;
            b2 = (vh - vb * k / q + k * k) / a0;
            a1 = 2 * (k * k - 1) / a0;
            a2 = (1 - k / q + k * k) / a0;

            f0 = 38.13547087602444;
            q = 0.5003270373238773;
            k = Math.Tan(Math.PI * f0 / rate);
            double n = 1 + k / q + k * k;
            c0 = 1; c1 = -2; c2 = 1;
            d1 = 2 * (k * k - 1) / n;
            d2 = (1 - k / q + k * k) / n;
        }

        public double Process(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            double v = c0 * y + c1 * u1 + c2 * u2 - d1 * v1 - d2 * v2;
            u2 = u1; u1 = y; v2 = v1; v1 = v;
            return v;
        }
    }
}

/// <summary>
/// Measured loudness per file, kept in a text file (one line per measurement, added at the end, so a crash loses at
/// most the last line). A file that changed since (size or date) is measured again. Thread-safe.
/// </summary>
public sealed class LoudnessStore
{
    readonly string _file;
    readonly Dictionary<string, (long Size, long Modified, LoudnessInfo Info)> _entries;
    readonly object _lock = new();
    int _lines;

    public LoudnessStore(string file)
    {
        _file = file;
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        _entries = new(comparer);
        try
        {
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadLines(file))
            {
                // lufs \t peak \t size \t modified ticks \t path
                var parts = line.Split('\t', 5);
                if (parts.Length == 5
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lufs)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double peak)
                    && long.TryParse(parts[2], out long size) && long.TryParse(parts[3], out long modified))
                {
                    _entries[parts[4]] = (size, modified, new LoudnessInfo(lufs, peak));
                    _lines++;
                }
            }
            if (_lines > _entries.Count * 2 + 1000) Compact();
        }
        catch
        {
            // An unreadable file: measure again.
        }
    }

    /// <summary>The file's loudness, if it was measured and hasn't changed since.</summary>
    public LoudnessInfo? Get(string path)
    {
        (long Size, long Modified, LoudnessInfo Info) e;
        lock (_lock)
            if (!_entries.TryGetValue(path, out e)) return null;
        var (size, modified) = Stamp(path);
        return size == e.Size && modified == e.Modified ? e.Info : null;
    }

    public void Set(string path, LoudnessInfo info)
    {
        var (size, modified) = Stamp(path);
        if (size < 0) return;
        lock (_lock)
        {
            _entries[path] = (size, modified, info);
            try
            {
                File.AppendAllText(_file, Line(path, size, modified, info), Encoding.UTF8);
                _lines++;
            }
            catch
            {
                // Best effort: it is measured again next time.
            }
        }
    }

    static string Line(string path, long size, long modified, LoudnessInfo info) =>
        string.Create(CultureInfo.InvariantCulture, $"{info.Lufs}\t{info.PeakDb}\t{size}\t{modified}\t{path}\n");

    /// <summary>Writes the file again with one line per file (it only grows otherwise).</summary>
    void Compact()
    {
        var text = new StringBuilder();
        foreach (var (path, e) in _entries) text.Append(Line(path, e.Size, e.Modified, e.Info));
        SafeFile.WriteAllText(_file, text.ToString());
        _lines = _entries.Count;
    }

    static (long Size, long Modified) Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : (-1, 0);
        }
        catch
        {
            return (-1, 0);
        }
    }
}

/// <summary>Measures files in the background, one at a time at low priority, so on-air playback never waits for it.</summary>
sealed class LoudnessWorker : IDisposable
{
    readonly LoudnessStore _store;
    readonly BlockingCollection<string> _queue = new();
    readonly ConcurrentDictionary<string, byte> _queued;
    readonly CancellationTokenSource _stop = new();
    readonly Thread _thread;

    public LoudnessWorker(LoudnessStore store)
    {
        _store = store;
        _queued = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        _thread = new Thread(Run) { Name = "Loudness", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Raised (on the worker thread) after a file was measured.</summary>
    public event Action<string, LoudnessInfo>? Measured;

    /// <summary>Queues a file to measure, unless it is already known or queued.</summary>
    public void Add(string path)
    {
        if (PlayoutEngine.IsUrl(path) || _store.Get(path) != null || !_queued.TryAdd(path, 0)) return;
        if (!_queue.IsAddingCompleted) _queue.Add(path);
    }

    /// <summary>Nothing waiting or being measured.</summary>
    public bool Idle => _queued.IsEmpty;

    void Run()
    {
        try
        {
            foreach (var path in _queue.GetConsumingEnumerable(_stop.Token))
            {
                try
                {
                    if (_store.Get(path) == null && File.Exists(path) && LoudnessMeter.Measure(path, _stop.Token) is { } info)
                    {
                        _store.Set(path, info);
                        Measured?.Invoke(path, info);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // Unreadable: played without levelling.
                }
                finally
                {
                    _queued.TryRemove(path, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _queue.CompleteAdding();
    }
}
