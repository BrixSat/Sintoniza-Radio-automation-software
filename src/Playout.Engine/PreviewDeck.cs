using ManagedBass;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>
/// Pre-listening without the playout engine (e.g. in the playlist manager): only the Preview output is opened, never
/// the Music and Cartwall cards, so a second program on the studio computer leaves the on-air sound alone. Files open
/// as <see cref="PreviewPlayer"/>s, paused at the start. Use from the UI thread; dispose when done.
/// </summary>
public sealed class PreviewDeck : IDisposable
{
    readonly AudioGraph _graph;

    /// <param name="forceDevice">Every output to this BASS device instead (0 = "no sound", for testing).</param>
    public PreviewDeck(AudioRouting routing, int? forceDevice = null)
    {
        PlayoutEngine.LoadPlugins();
        _graph = new AudioGraph(forceDevice);
        _graph.ApplyPreviewOnly(routing);
    }

    /// <summary>True when Preview is the same card and pair as Music or Cartwall: pre-listening would be heard on air.</summary>
    public bool PreviewIsOnAir => _graph.PreviewOnAir;

    /// <summary>A file to pre-listen (paused at the start), or null if it can't be read.</summary>
    public PreviewPlayer? Open(string path)
    {
        int h = Bass.CreateStream(SharedPaths.ToLocal(path), 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        if (h == 0) return null;
        if (!BassMix.MixerAddChannel(_graph.PreviewBus, h, BassFlags.MixerChanPause))
        {
            Bass.StreamFree(h);
            return null;
        }
        return new PreviewPlayer(_graph, h);
    }

    // The "no sound" device stays: in the same program, file lengths are read with it (AudioProbe).
    public void Dispose() => _graph.Free(keepNoSound: true);
}
