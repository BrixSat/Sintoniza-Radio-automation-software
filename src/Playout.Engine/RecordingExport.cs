using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>
/// A stretch of the station's recordings in one file (Sintoniza Rec → Export): from a time to a time, across as many
/// recording files as it takes, cut to the second. A gap (the recorder was stopped) is left out.
/// </summary>
public static class RecordingExport
{
    const int Rate = 44100;

    /// <param name="files">The recordings with when each starts (local time), in any order.</param>
    /// <returns>How many seconds were written.</returns>
    public static double Export(IReadOnlyList<(string Path, DateTime Start)> files, DateTime from, DateTime to, string output,
        RecFormat format, int bitrate, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (to <= from) throw new ArgumentException(L.T("The end is before the start."));
        PlayoutEngine.LoadPlugins();
        if (!Bass.Init(0) && Bass.LastError != Errors.Already) throw new InvalidOperationException(L.F("The audio library can't start ({0})", Bass.LastError));
        int mixer = BassMix.CreateMixerStream(Rate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop);
        if (mixer == 0) throw new InvalidOperationException(L.F("Can't export ({0})", Bass.LastError));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        int encoder = format == RecFormat.Wav
            ? BassEnc.EncodeStart(mixer, output, EncodeFlags.PCM | EncodeFlags.ConvertFloatTo16BitInt, (EncodeProcedure)null!, IntPtr.Zero)
            : BassEnc_Mp3.Start(mixer, $"-b {bitrate}", 0, output);
        if (encoder == 0)
        {
            Bass.StreamFree(mixer);
            throw new InvalidOperationException(L.F("Can't write {0} ({1})", Path.GetFileName(output), Bass.LastError));
        }
        double total = (to - from).TotalSeconds, written = 0;
        var buffer = new byte[Rate * 2 * 4]; // a second
        try
        {
            foreach (var (path, start) in files.OrderBy(f => f.Start))
            {
                cancel.ThrowIfCancellationRequested();
                int source = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
                if (source == 0) continue;
                double length = Bass.ChannelBytes2Seconds(source, Bass.ChannelGetLength(source));
                var end = start.AddSeconds(length);
                if (end <= from || start >= to) { Bass.StreamFree(source); continue; }
                double skip = Math.Max(0, (from - start).TotalSeconds);
                double seconds = (new[] { end, to }.Min() - new[] { start, from }.Max()).TotalSeconds;
                if (skip > 0) Bass.ChannelSetPosition(source, Bass.ChannelSeconds2Bytes(source, skip));
                BassMix.MixerAddChannel(mixer, source, BassFlags.MixerChanNoRampin | BassFlags.MixerChanDownMix);
                long bytes = (long)Math.Round(seconds * Rate) * 2 * 4;
                while (bytes > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    int got = Bass.ChannelGetData(mixer, buffer, (int)Math.Min(buffer.Length, bytes));
                    if (got <= 0) break;
                    bytes -= got;
                    written += got / (double)(Rate * 2 * 4);
                    progress?.Report(Math.Min(1, written / total));
                }
                BassMix.MixerRemoveChannel(source);
                Bass.StreamFree(source);
            }
        }
        finally
        {
            BassEnc.EncodeStop(encoder);
            Bass.StreamFree(mixer);
        }
        return written;
    }
}
