using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// Reads a file's length the way the player does (BASS, from the headers, without decoding), for the library.
/// Works without the playout engine running: BASS's no-sound device is enough to open decode streams.
/// </summary>
public static class AudioProbe
{
    static readonly Lazy<bool> Ready = new(() =>
    {
        try
        {
            PlayoutEngine.LoadPlugins();
            // Device 0 ("no sound") only serves decoding here; the engine picks its output devices explicitly.
            return Bass.Init(0) || Bass.LastError == Errors.Already;
        }
        catch (DllNotFoundException)
        {
            return false; // no BASS next to the program
        }
    });

    /// <summary>Length in seconds, or null when BASS isn't available or can't open the file.</summary>
    public static double? Duration(string path)
    {
        if (!Ready.Value) return null;
        int h = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        if (h == 0 && Bass.LastError == Errors.Init)
        {
            // Something in this program let go of BASS (an engine or a preview closed): open the "no sound" device again.
            Bass.Init(0);
            h = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
        }
        if (h == 0) return null;
        try
        {
            long bytes = Bass.ChannelGetLength(h, PositionFlags.Bytes);
            return bytes > 0 ? Bass.ChannelBytes2Seconds(h, bytes) : null;
        }
        finally
        {
            Bass.StreamFree(h);
        }
    }
}
