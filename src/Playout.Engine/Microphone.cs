using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>Web radio microphone: input device (empty = system default), mic gain, how far music and carts are ducked while talking, and the fade times.</summary>
public sealed record MicSettings(string Device = "", double GainDb = 0, double DuckDb = -15, double FadeDown = 0.3, double FadeUp = 1.0);

/// <summary>
/// The presenter's microphone, for web radio (on FM the mic goes through the desk). While it is open the mic goes into
/// Program, so it is in the stream but not on the outputs (the presenter would hear themselves late), and music and
/// carts are ducked. The input is only captured while the mic is open: every talk break starts with a fresh queue,
/// so the recording and output clocks never get far apart. Engine thread only, except the recording callback.
/// </summary>
sealed class Microphone
{
    const int RecordPeriodMs = 20;
    const double MaxQueuedSeconds = 0.25; // recording clock running ahead: drop blocks beyond this
    const int OpenFadeMs = 40;
    const int CloseFadeMs = 80;
    const double HighPassHz = 80; // cuts rumble, hum and handling noise, like the low-cut on a desk channel

    readonly AudioGraph _graph;
    readonly Action<double, Action> _after;
    readonly Action<string> _log;
    readonly RecordProcedure _recordProc; // strong reference: BASS holds a raw function pointer
    readonly HashSet<int> _initialised = new();

    MicSettings? _settings;
    int _record;
    volatile int _push;
    volatile float _gain = 1;
    volatile bool _live;
    int _session;
    float[] _buffer = [];
    readonly HighPass _highPass = new(HighPassHz, AudioGraph.SampleRate);

    public Microphone(AudioGraph graph, Action<double, Action> after, Action<string> log)
    {
        _graph = graph;
        _after = after;
        _log = log;
        _recordProc = OnRecord;
    }

    /// <summary>True while the mic is open (on air).</summary>
    public bool IsLive => _live;

    /// <summary>Names of the enabled input devices.</summary>
    public static IReadOnlyList<string> DeviceNames()
    {
        var list = new List<string>();
        for (int i = 0; Bass.RecordGetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled) list.Add(info.Name);
        return list;
    }

    /// <summary>Sets up the mic, or turns it off with null (FM: the mic goes through the desk).</summary>
    public void Configure(MicSettings? settings)
    {
        if (_push != 0)
        {
            _session++;
            _live = false;
            StopCapture();
            _graph.Duck(1, 300);
        }
        _settings = settings;
        _gain = settings == null ? 1 : DbToGain(settings.GainDb);
    }

    /// <summary>Opens or closes the mic: fades it in or out and ducks or restores music and carts.</summary>
    public void Set(bool on)
    {
        if (_settings is not { } s || on == _live) return;
        int session = ++_session;
        if (on)
        {
            if (_push == 0 && !StartCapture()) return;
            _live = true;
            Bass.ChannelSlideAttribute(_push, ChannelAttribute.Volume, 1, OpenFadeMs);
            _graph.Duck(DbToGain(s.DuckDb), Ms(s.FadeDown));
        }
        else
        {
            _live = false;
            Bass.ChannelSlideAttribute(_push, ChannelAttribute.Volume, 0, CloseFadeMs);
            _graph.Duck(1, Ms(s.FadeUp));
            // Keep capturing a moment, so a quick press again doesn't reopen the device.
            _after(CloseFadeMs / 1000.0 + 0.6, () => { if (_session == session) StopCapture(); });
        }
    }

    /// <summary>Mic peak level (0–1) while it is open, else 0.</summary>
    public float Level()
    {
        int push = _push;
        if (!_live || push == 0) return 0;
        var level = new float[1];
        BassMix.ChannelGetLevel(push, level, 0.05f, LevelRetrievalFlags.Mono);
        return Math.Min(1, level[0]);
    }

    public void Free()
    {
        StopCapture();
        foreach (int device in _initialised)
        {
            Bass.CurrentRecordingDevice = device;
            Bass.RecordFree();
        }
        _initialised.Clear();
    }

    bool StartCapture()
    {
        int device = Resolve(_settings!.Device);
        if (device < 0) { _log(L.T("Microphone: no input device found")); return false; }
        if (!_initialised.Contains(device))
        {
            if (!Bass.RecordInit(device) && Bass.LastError != Errors.Already)
            {
                _log(L.F("Microphone: cannot open the input ({0})", Bass.LastError));
                return false;
            }
            _initialised.Add(device);
        }
        Bass.CurrentRecordingDevice = device;

        int push = Bass.CreateStream(AudioGraph.SampleRate, 1, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        if (push == 0) { _log(L.F("Microphone: {0}", Bass.LastError)); return false; }
        Bass.ChannelSetAttribute(push, ChannelAttribute.Volume, 0);
        // ChanBuffer keeps recent data so the level can be measured.
        if (!BassMix.MixerAddChannel(_graph.Program, push, BassFlags.MixerChanBuffer))
        {
            _log(L.F("Microphone: {0}", Bass.LastError));
            Bass.StreamFree(push);
            return false;
        }
        _push = push;
        _highPass.Reset();

        _record = Bass.RecordStart(AudioGraph.SampleRate, 1, BassFlags.Float, RecordPeriodMs, _recordProc, IntPtr.Zero);
        if (_record == 0)
        {
            _log(L.F("Microphone: cannot start recording ({0})", Bass.LastError));
            StopCapture();
            return false;
        }
        return true;
    }

    void StopCapture()
    {
        if (_record != 0) { Bass.ChannelStop(_record); _record = 0; }
        int push = _push;
        _push = 0;
        if (push != 0) Bass.StreamFree(push);
    }

    /// <summary>Recording thread: filters, applies the gain and queues the block for the mixer.</summary>
    bool OnRecord(int handle, IntPtr data, int length, IntPtr user)
    {
        int push = _push;
        if (push == 0 || length <= 0) return true;
        int queued = Bass.StreamPutData(push, IntPtr.Zero, 0);
        if (queued > MaxQueuedSeconds * AudioGraph.SampleRate * sizeof(float)) return true;

        int count = length / sizeof(float);
        if (_buffer.Length < count) _buffer = new float[count];
        Marshal.Copy(data, _buffer, 0, count);
        float gain = _gain;
        for (int i = 0; i < count; i++) _buffer[i] = _highPass.Process(_buffer[i]) * gain;
        Bass.StreamPutData(push, _buffer, count * sizeof(float));
        return true;
    }

    static int Resolve(string device)
    {
        int first = -1, systemDefault = -1;
        for (int i = 0; Bass.RecordGetDeviceInfo(i, out var info); i++)
        {
            if (!info.IsEnabled) continue;
            if (!string.IsNullOrEmpty(device) && info.Name == device) return i;
            if (first < 0) first = i;
            if (info.IsDefault && systemDefault < 0) systemDefault = i;
        }
        return systemDefault >= 0 ? systemDefault : first;
    }

    /// <summary>Second-order Butterworth high-pass (RBJ biquad).</summary>
    sealed class HighPass
    {
        readonly double _b0, _b1, _b2, _a1, _a2;
        double _x1, _x2, _y1, _y2;

        public HighPass(double hz, int sampleRate)
        {
            double w = 2 * Math.PI * hz / sampleRate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            double a0 = 1 + alpha;
            _b0 = (1 + cos) / 2 / a0;
            _b1 = -(1 + cos) / a0;
            _b2 = _b0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

        public float Process(float x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return (float)y;
        }
    }

    static float DbToGain(double db) => (float)Math.Pow(10, db / 20);

    static int Ms(double seconds) => (int)(Math.Max(0, seconds) * 1000);
}
