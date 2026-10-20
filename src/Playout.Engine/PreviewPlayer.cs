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

    readonly List<int> _syncs = new();
    // Kept alive while BASS may call them.
    readonly List<SyncProcedure> _procedures = new();

    /// <summary>
    /// A join as on air (the segue editor): when this reaches <paramref name="at"/> seconds, <paramref name="next"/>
    /// starts (sample-accurate), and this fades out over <paramref name="fadeOut"/> (null: plays on unfaded); at
    /// <paramref name="stopAt"/> (its Cue Out) it stops. Replaces an earlier join; Play this one to hear it.
    /// </summary>
    public void Join(double at, PreviewPlayer next, double? fadeOut, double stopAt)
    {
        if (_handle == 0) return;
        ClearJoin();
        int h = _handle;
        SyncProcedure start = (_, _, _, _) =>
        {
            if (next._handle != 0) BassMix.ChannelRemoveFlag(next._handle, BassFlags.MixerChanPause);
            if (fadeOut is > 0) Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, 0, (int)(fadeOut.Value * 1000));
        };
        SyncProcedure stop = (_, _, _, _) => Bass.ChannelSlideAttribute(h, ChannelAttribute.Volume, 0, 40);
        _procedures.Add(start);
        _procedures.Add(stop);
        _syncs.Add(BassMix.ChannelSetSync(h, SyncFlags.Position | SyncFlags.Mixtime | SyncFlags.Onetime, Bass.ChannelSeconds2Bytes(h, at), start));
        if (stopAt < Length - 0.01)
            _syncs.Add(BassMix.ChannelSetSync(h, SyncFlags.Position | SyncFlags.Mixtime | SyncFlags.Onetime, Bass.ChannelSeconds2Bytes(h, stopAt), stop));
    }

    /// <summary>Takes away a join, at full volume again.</summary>
    public void ClearJoin()
    {
        if (_handle == 0) return;
        foreach (int sync in _syncs) BassMix.ChannelRemoveSync(_handle, sync);
        _syncs.Clear();
        _procedures.Clear();
        Bass.ChannelSetAttribute(_handle, ChannelAttribute.Volume, 1);
    }

    public void Dispose()
    {
        int h = Interlocked.Exchange(ref _handle, 0);
        if (h != 0) Bass.StreamFree(h);
    }
}
