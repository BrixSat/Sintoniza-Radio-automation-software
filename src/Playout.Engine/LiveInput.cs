using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ManagedBass;

namespace Playout.Engine;

/// <summary>
/// An input of this computer played on air as an item ("input:Line In"): the desk's line, a satellite receiver, a
/// remote studio. It goes through the same route as an internet stream — for a set time or until Next, put in by hand,
/// from the daily playlists or as a scheduled event. The input is captured only while the item is on air.
/// </summary>
public static class LiveInput
{
    public const string Prefix = "input:";
    const int RecordPeriodMs = 20;
    const double MaxQueuedSeconds = 0.3; // the input's clock running ahead of the output's: blocks beyond this are dropped

    /// <summary>The captures on air: the push stream the mixer plays, and what feeds it.</summary>
    static readonly ConcurrentDictionary<int, Capture> Captures = new();

    sealed class Capture
    {
        public int Record;
        public RecordProcedure Proc = null!; // strong reference: BASS holds a raw function pointer
        public SyncProcedure Freed = null!;
        public byte[] Buffer = [];
    }

    public static bool Is(string? path) => path != null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>"input:Line In" for that device; "input:" for the system's default input.</summary>
    public static string PathFor(string device) => Prefix + device;

    /// <summary>The device an item's path names ("" = the system default).</summary>
    public static string DeviceOf(string path) => Is(path) ? path[Prefix.Length..].Trim() : "";

    /// <summary>
    /// Starts capturing the input and returns a decoding stream of it for the mixer (stereo, the graph's rate). The
    /// capture stops when the stream is freed. Throws when the input can't be opened.
    /// </summary>
    public static int Open(string path)
    {
        string name = DeviceOf(path);
        int device = Microphone.Resolve(name);
        if (device < 0) throw new IOException(L.T("no input device found"));
        if (name.Length > 0 && Bass.RecordGetDeviceInfo(device, out var info) && info.Name != name)
            throw new IOException(L.F("the input \"{0}\" isn't on this computer", name));
        if (!Bass.RecordInit(device) && Bass.LastError != Errors.Already) throw new IOException(Bass.LastError.ToString());
        Bass.CurrentRecordingDevice = device;

        int push = Bass.CreateStream(AudioGraph.SampleRate, 2, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        if (push == 0) throw new IOException(Bass.LastError.ToString());
        var capture = new Capture();
        capture.Proc = (_, data, length, _) =>
        {
            if (length <= 0) return true;
            if (Bass.StreamPutData(push, IntPtr.Zero, 0) > MaxQueuedSeconds * AudioGraph.SampleRate * 2 * sizeof(float)) return true;
            if (capture.Buffer.Length < length) capture.Buffer = new byte[length];
            Marshal.Copy(data, capture.Buffer, 0, length);
            Bass.StreamPutData(push, capture.Buffer, length);
            return true;
        };
        capture.Freed = (_, _, _, _) =>
        {
            if (Captures.TryRemove(push, out var c) && c.Record != 0) Bass.ChannelStop(c.Record);
        };
        Bass.ChannelSetSync(push, SyncFlags.Free, 0, capture.Freed);
        Captures[push] = capture;
        capture.Record = Bass.RecordStart(AudioGraph.SampleRate, 2, BassFlags.Float, RecordPeriodMs, capture.Proc, IntPtr.Zero);
        if (capture.Record == 0)
        {
            var error = Bass.LastError;
            Bass.StreamFree(push);
            throw new IOException(L.F("can't capture the input ({0})", error));
        }
        return push;
    }
}
