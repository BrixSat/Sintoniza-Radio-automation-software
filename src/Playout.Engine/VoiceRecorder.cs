using ManagedBass;
using ManagedBass.Enc;

namespace Playout.Engine;

/// <summary>
/// Records a voice track from an input (the studio mic, or any input of the computer) straight into an MP3 file, with
/// its level for a meter. Never touches the on-air outputs. Use from one thread; dispose when done.
/// </summary>
public sealed class VoiceRecorder : IDisposable
{
    const int SampleRate = 44100;
    readonly int _device;
    readonly RecordProcedure _proc = (_, _, _, _) => true; // kept alive while BASS calls it
    int _record;
    long _startedTicks;

    /// <param name="device">An input's name (empty or not found: the computer's default input).</param>
    public VoiceRecorder(string device)
    {
        _device = Microphone.Resolve(device);
        if (_device < 0) throw new InvalidOperationException(L.T("No input to record from was found."));
        if (!Bass.RecordInit(_device) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException(L.F("The input can't be opened ({0})", Bass.LastError));
    }

    /// <summary>The inputs that can be recorded from.</summary>
    public static IReadOnlyList<string> Devices() => Microphone.DeviceNames();

    public bool IsRecording => _record != 0;

    /// <summary>Seconds recorded so far.</summary>
    public double Seconds => _record == 0 ? 0 : (Environment.TickCount64 - _startedTicks) / 1000.0;

    /// <summary>The input's level now (0–1).</summary>
    public float Level
    {
        get
        {
            if (_record == 0) return 0;
            var level = new float[1];
            return Bass.ChannelGetLevel(_record, level, 0.05f, LevelRetrievalFlags.Mono) ? Math.Min(1, level[0]) : 0;
        }
    }

    /// <summary>Starts recording into <paramref name="file"/> (MP3, mono, 160 kbps).</summary>
    public void Start(string file)
    {
        Stop();
        Bass.CurrentRecordingDevice = _device;
        int record = Bass.RecordStart(SampleRate, 1, BassFlags.Default, 20, _proc, IntPtr.Zero);
        if (record == 0) throw new InvalidOperationException(L.F("Recording can't start ({0})", Bass.LastError));
        if (BassEnc_Mp3.Start(record, "-b 160", EncodeFlags.AutoFree, file) == 0)
        {
            var error = Bass.LastError;
            Bass.ChannelStop(record);
            throw new InvalidOperationException(L.F("The recording can't be written ({0})", error));
        }
        _record = record;
        _startedTicks = Environment.TickCount64;
    }

    /// <summary>Stops: the file is finished (the encoder closes it).</summary>
    public void Stop()
    {
        int record = Interlocked.Exchange(ref _record, 0);
        if (record != 0) Bass.ChannelStop(record);
    }

    public void Dispose()
    {
        Stop();
        Bass.CurrentRecordingDevice = _device;
        Bass.RecordFree();
    }
}
