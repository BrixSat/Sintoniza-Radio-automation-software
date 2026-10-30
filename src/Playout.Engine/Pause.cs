using System.Runtime.InteropServices;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// A pause in the playlist ("pause:"): an item with no sound, for a set time or until Next, while the presenter talks.
/// Its title is what to talk about. It goes through the same route as a stream (see <see cref="TrackKind.Pause"/>):
/// silence for the mixer, so the times, the countdown and what comes after work as for any item.
/// </summary>
public static class Pause
{
    public const string Path = "pause:";

    public static bool Is(string? path) => path != null && path.Trim().Equals(Path, StringComparison.OrdinalIgnoreCase);

    static readonly byte[] Zeros = new byte[64 * 1024];

    // Strong reference: BASS holds a raw function pointer.
    static readonly StreamProcedure Silence = (_, buffer, length, _) =>
    {
        if (length <= 0) return 0;
        for (int i = 0; i < length; i += Zeros.Length) Marshal.Copy(Zeros, 0, buffer + i, Math.Min(Zeros.Length, length - i));
        return length;
    };

    /// <summary>A decoding stream of silence for the mixer (stereo, the graph's rate), endless: the item's time ends it.</summary>
    public static int Open()
    {
        int handle = Bass.CreateStream(AudioGraph.SampleRate, 2, BassFlags.Decode | BassFlags.Float, Silence, IntPtr.Zero);
        return handle != 0 ? handle : throw new IOException(Bass.LastError.ToString());
    }
}
