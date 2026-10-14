using System.Text.Json;

namespace Playout.Engine;

/// <summary>
/// Cue points per file, kept in a JSON file (path → markers). The audio files themselves are never written to.
/// Thread-safe; saving happens off the calling thread.
/// </summary>
public sealed class CueStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly string _file;
    readonly Dictionary<string, CuePoints> _cues;
    readonly object _lock = new();

    public CueStore(string file)
    {
        _file = file;
        // Paths are case-insensitive on Windows and (by default) macOS.
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        _cues = new Dictionary<string, CuePoints>(comparer);
        // A damaged cue file must never stop the station from starting: then the backup, or no cue points.
        if (SafeFile.Read(file, json => JsonSerializer.Deserialize<Dictionary<string, CuePoints>>(json)) is { } saved)
            foreach (var (path, cues) in saved) _cues[path] = cues;
    }

    public CuePoints? Get(string path)
    {
        lock (_lock) return _cues.GetValueOrDefault(path);
    }

    /// <summary>Stores the markers for a file (empty markers remove it) and saves in the background.</summary>
    public void Set(string path, CuePoints cues)
    {
        string json;
        lock (_lock)
        {
            if (cues.IsEmpty) _cues.Remove(path);
            else _cues[path] = cues;
            json = JsonSerializer.Serialize(_cues, Json);
        }
        Task.Run(() => Save(json));
    }

    readonly object _saveLock = new();

    void Save(string json)
    {
        lock (_saveLock)
        {
            try
            {
                SafeFile.WriteAllText(_file, json);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}

/// <summary>Suggests Cue In, Cue Out and Mix from a file's waveform.</summary>
public static class CueDetector
{
    /// <summary>Quieter than this (about -40 dBFS) counts as silence.</summary>
    const float SilencePeak = 0.01f;

    /// <summary>The ending counts as faded once it is this far under the track's median loudness (about -15 dB).</summary>
    const float FadedRatio = 0.18f;

    /// <summary>The Mix point is never put further back than this from Cue Out.</summary>
    const double MaxOverlap = 15;

    /// <summary>
    /// Cue In at the first sound, Cue Out after the last, and Mix where a fade-out has dropped well below the
    /// track's typical level (on a hard ending that is right at Cue Out). Intro and Outro are kept as they are.
    /// </summary>
    public static CuePoints Detect(Waveform w, CuePoints current)
    {
        int first = Array.FindIndex(w.Peak, p => p >= SilencePeak);
        int last = Array.FindLastIndex(w.Peak, p => p >= SilencePeak);
        if (first < 0 || last <= first) return current;

        double pps = w.PointsPerSecond;
        double cueIn = first / pps;
        double cueOut = (last + 1) / pps;

        var sorted = w.Rms[first..(last + 1)].Order().ToArray();
        float threshold = sorted[sorted.Length / 2] * FadedRatio;
        int loud = last;
        while (loud > first && w.Rms[loud] < threshold) loud--;
        double mix = Math.Max((loud + 1) / pps, cueOut - MaxOverlap);

        return current with
        {
            CueIn = Math.Round(cueIn, 2),
            CueOut = Math.Round(cueOut, 2),
            Mix = Math.Round(Math.Clamp(mix, cueIn, cueOut), 2),
        };
    }
}
