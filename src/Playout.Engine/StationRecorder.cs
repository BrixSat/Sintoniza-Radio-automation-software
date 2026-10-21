using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Enc;

namespace Playout.Engine;

/// <summary>How a recording is written: MP3 at a bitrate, or WAV (16-bit PCM).</summary>
public enum RecFormat { Mp3, Wav }

/// <summary>
/// Records the station as it goes out (Sintoniza Rec): from an input of the computer (the desk's programme output
/// into the sound card) or from the station's own stream (a URL). Gives its level for meters, can be heard on an
/// output (monitoring), and writes the audio into files: <see cref="StartFile"/> adds a writer, <see cref="StopFile"/>
/// finishes one — a new file is started before the old one is stopped, so nothing is lost between files.
/// </summary>
public sealed class StationRecorder : IDisposable
{
    const int SampleRate = 44100;
    readonly string _input;
    readonly string? _url;
    readonly RecordProcedure _recordProc = (_, _, _, _) => true; // kept alive while BASS calls it
    readonly DSPProcedure _dspProc;
    readonly object _lock = new();
    int _channel, _inputDevice = -1, _monitor;
    Thread? _pump;
    volatile bool _running;
    float _peakLeft, _peakRight;
    long _lastData;

    /// <summary>What happened (started, the stream dropped and came back…), for the log.</summary>
    public event Action<string>? Log;

    /// <param name="input">An input's name (empty: the computer's default input); used when <paramref name="url"/> is null.</param>
    /// <param name="url">The station's stream to record instead of an input.</param>
    public StationRecorder(string input, string? url)
    {
        _input = input;
        _url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        _dspProc = Dsp;
    }

    /// <summary>The inputs that can be recorded from, and the outputs it can be heard on.</summary>
    public static IReadOnlyList<string> Inputs() => Microphone.DeviceNames();
    public static IReadOnlyList<string> Outputs() => AudioGraph.DeviceNames();

    public bool IsRunning => _running;

    /// <summary>Where it records from, for the screen: "Input: Line In (USB Audio)" / "Stream: http://…".</summary>
    public string Source => _url != null ? L.F("Stream: {0}", _url) : L.F("Input: {0}", _input.Length > 0 ? _input : L.T("the default input"));

    /// <summary>The audio format being recorded ("44100 Hz, stereo"), once started.</summary>
    public string Format { get; private set; } = "";

    /// <summary>Seconds since audio last arrived (a stream that dropped, an input that stalled).</summary>
    public double SecondsWithoutData => _running ? (Environment.TickCount64 - Interlocked.Read(ref _lastData)) / 1000.0 : 0;

    /// <summary>The highest level (0–1, left and right) since this was last asked.</summary>
    public (float Left, float Right) TakePeaks()
    {
        lock (_lock)
        {
            var peaks = (_peakLeft, _peakRight);
            _peakLeft = _peakRight = 0;
            return peaks;
        }
    }

    public void Start()
    {
        if (_running) return;
        PlayoutEngine.LoadPlugins();
        if (!Bass.Init(0) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException(L.F("The audio library can't start ({0})", Bass.LastError));
        _running = true;
        Interlocked.Exchange(ref _lastData, Environment.TickCount64);
        if (_url != null)
        {
            _pump = new Thread(PumpStream) { IsBackground = true, Name = "Sintoniza Rec stream" };
            _pump.Start();
            return;
        }
        _inputDevice = Microphone.Resolve(_input);
        if (_inputDevice < 0) { _running = false; throw new InvalidOperationException(L.T("No input to record from was found.")); }
        if (!Bass.RecordInit(_inputDevice) && Bass.LastError != Errors.Already)
        {
            _running = false;
            throw new InvalidOperationException(L.F("The input can't be opened ({0})", Bass.LastError));
        }
        Bass.CurrentRecordingDevice = _inputDevice;
        int channel = Bass.RecordStart(SampleRate, 2, BassFlags.Float, 50, _recordProc, IntPtr.Zero);
        if (channel == 0)
        {
            _running = false;
            throw new InvalidOperationException(L.F("Recording can't start ({0})", Bass.LastError));
        }
        Attach(channel);
        Log?.Invoke(L.F("Recording from {0}", Source));
    }

    void Attach(int channel)
    {
        Bass.ChannelSetDSP(channel, _dspProc, IntPtr.Zero, 1);
        var info = Bass.ChannelGetInfo(channel);
        Format = L.F("{0} Hz, {1}", info.Frequency, info.Channels == 1 ? L.T("mono") : L.T("stereo"));
        lock (_lock) _channel = channel;
        MonitorAttach();
    }

    /// <summary>A stream: decoded here (that is what feeds the writers), opened again when it drops.</summary>
    void PumpStream()
    {
        var buffer = new byte[65536];
        while (_running)
        {
            int h = Bass.CreateStream(_url!, 0, BassFlags.Decode | BassFlags.Float | BassFlags.StreamDownloadBlocks, null, IntPtr.Zero);
            if (h == 0)
            {
                Log?.Invoke(L.F("The stream can't be opened ({0}); trying again in 5 s", Bass.LastError));
                Sleep(5000);
                continue;
            }
            Log?.Invoke(L.F("Recording from {0}", Source));
            // The writers stay on the old channel until it is gone: they move to the new one.
            int[] writers;
            lock (_lock) { writers = _writers.Keys.ToArray(); }
            int old = _channel;
            Attach(h);
            foreach (int w in writers) if (_writers.TryGetValue(w, out var spec)) Restart(w, spec);
            if (old != 0) Bass.StreamFree(old);
            while (_running)
            {
                int n = Bass.ChannelGetData(h, buffer, buffer.Length);
                if (n < 0) break; // ended or failed
                if (n == 0) Thread.Sleep(20);
            }
            if (!_running) break;
            Log?.Invoke(L.T("The stream stopped; opening it again"));
            Sleep(2000);
        }
    }

    void Sleep(int ms)
    {
        for (int i = 0; i < ms / 100 && _running; i++) Thread.Sleep(100);
    }

    /// <summary>The level and the monitor, from the audio as it passes.</summary>
    void Dsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (length <= 0) return;
        Interlocked.Exchange(ref _lastData, Environment.TickCount64);
        int samples = length / 4;
        var data = new float[samples];
        Marshal.Copy(buffer, data, 0, samples);
        float left = 0, right = 0;
        for (int i = 0; i + 1 < samples; i += 2)
        {
            left = Math.Max(left, Math.Abs(data[i]));
            right = Math.Max(right, Math.Abs(data[i + 1]));
        }
        lock (_lock)
        {
            _peakLeft = Math.Max(_peakLeft, left);
            _peakRight = Math.Max(_peakRight, right);
        }
        if (_monitor != 0) Bass.StreamPutData(_monitor, buffer, length);
    }

    // ───────────────────────── Files ─────────────────────────

    sealed record Writer(string Path, RecFormat Format, int Bitrate, bool Mono, int Handle);
    readonly Dictionary<int, Writer> _writers = new();
    int _nextWriter;

    /// <summary>Starts writing the audio into <paramref name="path"/>; returns its id (for <see cref="StopFile"/>).</summary>
    public int StartFile(string path, RecFormat format, int bitrate = 128, bool mono = false)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        int handle = Open(path, format, bitrate, mono);
        lock (_lock)
        {
            int id = ++_nextWriter;
            _writers[id] = new Writer(path, format, bitrate, mono, handle);
            return id;
        }
    }

    int Open(string path, RecFormat format, int bitrate, bool mono)
    {
        int channel;
        lock (_lock) channel = _channel;
        if (channel == 0) return 0; // a stream not connected yet: started when it is
        int handle = format == RecFormat.Wav
            ? BassEnc.EncodeStart(channel, path, EncodeFlags.PCM | EncodeFlags.ConvertFloatTo16BitInt | (mono ? EncodeFlags.Mono : 0), (EncodeProcedure)null!, IntPtr.Zero)
            : BassEnc_Mp3.Start(channel, $"-b {bitrate}{(mono ? " -m m" : "")}", mono ? EncodeFlags.Mono : 0, path);
        if (handle == 0) Log?.Invoke(L.F("Can't write {0} ({1})", System.IO.Path.GetFileName(path), Bass.LastError));
        return handle;
    }

    /// <summary>A writer on a stream that came back: on with a new part of the file (the old part kept as it is).</summary>
    void Restart(int id, Writer w)
    {
        if (w.Handle != 0) BassEnc.EncodeStop(w.Handle);
        string path = w.Handle == 0 ? w.Path : NextPart(w.Path);
        int handle = Open(path, w.Format, w.Bitrate, w.Mono);
        lock (_lock) _writers[id] = w with { Path = path, Handle = handle };
    }

    /// <summary>file.mp3 → file-2.mp3 (or -3…), the next free name.</summary>
    static string NextPart(string path)
    {
        string dir = System.IO.Path.GetDirectoryName(path)!, name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string next = System.IO.Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(next)) return next;
        }
    }

    /// <summary>Finishes a file.</summary>
    public void StopFile(int id)
    {
        Writer? w;
        lock (_lock)
        {
            if (!_writers.Remove(id, out w)) return;
        }
        if (w.Handle != 0) BassEnc.EncodeStop(w.Handle);
    }

    /// <summary>Whether a file is being written (false: it couldn't be opened, or the disk failed).</summary>
    public bool IsWriting(int id)
    {
        lock (_lock)
            return _writers.TryGetValue(id, out var w) && w.Handle != 0 && BassEnc.EncodeIsActive(w.Handle) != PlaybackState.Stopped;
    }

    // ───────────────────────── Monitor ─────────────────────────

    string? _monitorOutput;

    /// <summary>Hear the recording on an output (null: off).</summary>
    public void SetMonitor(string? output)
    {
        _monitorOutput = output;
        MonitorAttach();
    }

    void MonitorAttach()
    {
        int old = Interlocked.Exchange(ref _monitor, 0);
        if (old != 0) Bass.StreamFree(old);
        int channel;
        lock (_lock) channel = _channel;
        if (_monitorOutput == null || channel == 0) return;
        int device = 1;
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled && (info.Name == _monitorOutput || (_monitorOutput.Length == 0 && info.IsDefault))) { device = i; break; }
        if (!Bass.Init(device) && Bass.LastError != Errors.Already) { Log?.Invoke(L.F("Can't open {0} ({1})", _monitorOutput, Bass.LastError)); return; }
        var ci = Bass.ChannelGetInfo(channel);
        Bass.CurrentDevice = device;
        int stream = Bass.CreateStream(ci.Frequency, ci.Channels, BassFlags.Float, StreamProcedureType.Push);
        if (stream == 0) return;
        Bass.ChannelPlay(stream);
        _monitor = stream;
    }

    // ───────────────────────── Stop ─────────────────────────

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _pump?.Join(3000);
        int[] ids;
        lock (_lock) ids = _writers.Keys.ToArray();
        foreach (int id in ids) StopFile(id);
        int monitor = Interlocked.Exchange(ref _monitor, 0);
        if (monitor != 0) Bass.StreamFree(monitor);
        int channel;
        lock (_lock) { channel = _channel; _channel = 0; }
        if (channel != 0)
        {
            if (_url != null) Bass.StreamFree(channel);
            else Bass.ChannelStop(channel);
        }
        if (_inputDevice >= 0)
        {
            Bass.CurrentRecordingDevice = _inputDevice;
            Bass.RecordFree();
            _inputDevice = -1;
        }
        Log?.Invoke(L.T("Recording stopped"));
    }

    public void Dispose() => Stop();
}
