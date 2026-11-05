using System.Text.Json;

namespace Playout.Engine;

/// <summary>
/// Cue points per file, kept in a JSON file (path → markers). The audio files themselves are never written to.
/// With a music library the app also keeps them there (see <see cref="Edited"/> and <see cref="Apply"/>), so
/// studios sharing a library share cue points; the file is this computer's copy for when the library can't be reached.
/// Thread-safe; saving happens off the calling thread.
/// </summary>
public sealed class CueStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly string _file;
    readonly Dictionary<string, CuePoints> _cues;
    readonly object _lock = new();

    /// <summary>Raised after <see cref="Set"/> (on the caller's thread): cue points were edited here.</summary>
    public event Action<string, CuePoints>? Edited;

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

    /// <summary>The station works with auto cue's cue points: off, <see cref="Get"/> leaves them out.</summary>
    public bool UseAuto { get; set; }

    /// <summary>A file's cue points (auto cue's only while <see cref="UseAuto"/>; never its mark that there is nothing to cut).</summary>
    public CuePoints? Get(string path) => GetAny(path) is { } cues && (!cues.Auto || UseAuto && cues.HasValues) ? cues : null;

    /// <summary>Stores auto cue's cue points only if the file has none (set by hand meanwhile: those stay). Whether it did.</summary>
    public bool SetIfNone(string path, CuePoints cues)
    {
        lock (_lock)
        {
            if (_cues.ContainsKey(path)) return false;
            _cues[path] = cues;
        }
        Task.Run(Save);
        Edited?.Invoke(path, cues);
        return true;
    }

    /// <summary>
    /// Takes out auto cue's cue points for a file (they were made for another file under its name), if they are still
    /// there, so they are made again. Not sent to the library: the new ones go over them there (each studio and Sintoniza
    /// Pro see by the length that the old ones aren't the file's).
    /// </summary>
    public void DropAuto(string path, CuePoints cues)
    {
        lock (_lock)
        {
            if (_cues.GetValueOrDefault(path) != cues) return;
            _cues.Remove(path);
        }
        Task.Run(Save);
    }

    /// <summary>A file's cue points, set by hand or by auto cue (or the mark that auto cue found nothing to cut).</summary>
    public CuePoints? GetAny(string path)
    {
        lock (_lock) return _cues.GetValueOrDefault(path);
    }

    /// <summary>Stores the markers for a file (empty markers remove it) and saves in the background.</summary>
    public void Set(string path, CuePoints cues)
    {
        lock (_lock)
        {
            if (cues.IsEmpty) _cues.Remove(path);
            else _cues[path] = cues;
        }
        Task.Run(Save);
        Edited?.Invoke(path, cues);
    }

    /// <summary>Every file's cue points.</summary>
    public List<(string Path, CuePoints Cues)> All()
    {
        lock (_lock) return _cues.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Takes cue points that were set elsewhere (another studio, via the library); empty ones remove them.
    /// Files in <paramref name="keep"/> (edited here, not saved to the library yet) keep theirs. Returns the files
    /// whose cue points changed.
    /// </summary>
    public List<string> Apply(IEnumerable<(string Path, CuePoints Cues)> changes, ICollection<string> keep)
    {
        var changed = new List<string>();
        lock (_lock)
        {
            foreach (var (path, cues) in changes)
            {
                if (keep.Contains(path)) continue;
                var current = _cues.GetValueOrDefault(path) ?? CuePoints.None;
                if (current == cues) continue;
                if (cues.IsEmpty) _cues.Remove(path);
                else _cues[path] = cues;
                changed.Add(path);
            }
        }
        if (changed.Count > 0) Task.Run(Save);
        return changed;
    }

    readonly object _saveLock = new();

    /// <summary>Writes what is stored now, so saves that run out of order still end with the latest.</summary>
    void Save()
    {
        lock (_saveLock)
        {
            try
            {
                string json;
                lock (_lock) json = JsonSerializer.Serialize(_cues, Json);
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

    /// <summary>
    /// Auto cue's cue points for a song (<see cref="CuePoints.Auto"/>): only what is worth cutting. Cue In past
    /// silence at the start (up to 10 s of it), Cue Out before silence at the end, and Mix on a fade-out (on a hard
    /// ending none: the station's crossfade, from the new Cue Out). None of them: nothing to cut (and it is not looked
    /// at again).
    /// </summary>
    /// <param name="size">The file's size (bytes), kept with them.</param>
    public static CuePoints Auto(Waveform w, long? size = null)
    {
        var found = Detect(w, CuePoints.None);
        double length = w.Duration;
        double? cueIn = found.CueIn is > 0.05 and < 10 ? found.CueIn : null;
        double start = cueIn ?? 0;
        double? cueOut = found.CueOut is { } o && o < length - 0.1 && o - start > 20 ? o : null;
        double end = cueOut ?? length;
        double? mix = found.Mix is { } m && m < end - 0.5 && m - start > 20 ? m : null;
        return new CuePoints(CueIn: cueIn, Mix: mix, CueOut: cueOut, Auto: true, FileSize: size);
    }
}
