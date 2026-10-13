using ManagedBass;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>
/// Pre-listens one file on the Preview output, e.g. while setting cue points. Never goes to the stream.
/// Call from one thread (the UI); dispose when done.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    readonly AudioGraph _graph;
    int _handle;

    internal PreviewPlayer(AudioGraph graph, int handle)
    {
        _graph = graph;
        _handle = handle;
        Length = Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle, PositionFlags.Bytes));
    }

    public double Length { get; }

    public bool IsPlaying =>
        _handle != 0 && !BassMix.ChannelHasFlag(_handle, BassFlags.MixerChanPause)
        && Bass.ChannelIsActive(_handle) == PlaybackState.Playing;

    /// <summary>What is audible now, corrected for output latency.</summary>
    public double Position
    {
        get
        {
            if (_handle == 0) return 0;
            long p = BassMix.ChannelGetPosition(_handle, PositionFlags.Bytes, _graph.PreviewDelay);
            return p < 0 ? 0 : Bass.ChannelBytes2Seconds(_handle, p);
        }
    }

    public void Play()
    {
        if (_handle == 0) return;
        // Pressing Play at the end starts again from the top.
        if (Bass.ChannelIsActive(_handle) == PlaybackState.Stopped) Seek(0);
        BassMix.ChannelRemoveFlag(_handle, BassFlags.MixerChanPause);
    }

    public void Pause()
    {
        if (_handle != 0) BassMix.ChannelAddFlag(_handle, BassFlags.MixerChanPause);
    }

    public void Seek(double seconds)
    {
        if (_handle == 0) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, Length - 0.01));
        BassMix.ChannelSetPosition(_handle, Bass.ChannelSeconds2Bytes(_handle, seconds), PositionFlags.Bytes | PositionFlags.MixerReset);
    }

    public void Dispose()
    {
        int h = Interlocked.Exchange(ref _handle, 0);
        if (h != 0) Bass.StreamFree(h);
    }
}
