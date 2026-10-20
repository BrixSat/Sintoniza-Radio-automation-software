using ManagedBass;
using ManagedBass.Mix;

namespace Playout.Engine;

/// <summary>Where a bus goes: a sound card (by name; empty = system default) and the first channel of a stereo pair (0 = outputs 1–2, 2 = 3–4, …).</summary>
public sealed record OutputRoute(string Device = "", int FirstChannel = 0);

/// <summary>Output routing for every bus. Music and Cartwall can go to different desk channels; Preview is for pre-listening (never on air).</summary>
public sealed record AudioRouting(OutputRoute Music, OutputRoute Carts, OutputRoute Preview)
{
    public static AudioRouting Default => new(new OutputRoute(), new OutputRoute(), new OutputRoute());
}

/// <summary>
/// The mixing graph:
/// <code>
///   tracks ─► MusicBus ─┬─ split ─► device mixer (card A, channels n..n+1) ─► speakers / desk fader
///                       └─ split ─┐
///   carts ──► CartBus ──┬─ split ─► device mixer (card B, channels m..m+1)
///                       └─ split ─┴► Program (sum) ─► encoder (stream)
///   preview ► PreviewBus ───────► device mixer (card C, channels p..p+1)   (pre-listen; not in Program)
/// </code>
/// Buses and Program are decode mixers that live for the whole session; only the device mixers are rebuilt when
/// the routing changes. Program is attached (silently, all-zero matrix) to the music card's mixer, so that card's
/// clock drives the stream. If the buses are on different cards their clocks can drift apart slightly over many
/// hours; the split buffer absorbs it, but for long-running setups on two cards a desk return input is the more
/// robust stream source.
/// </summary>
sealed class AudioGraph
{
    public const int SampleRate = 44100;
    const int SplitBufferMs = 4000;

    public int MusicBus { get; }
    public int CartBus { get; }
    public int Program { get; }
    public int PreviewBus { get; }

    readonly int _musicOut, _cartOut;   // splits that feed the device mixers
    readonly int _musicProg, _cartProg; // splits that feed Program
    readonly int? _forcedDevice;

    // device number -> (mixer handle, channel count). Rebuilt by Apply.
    readonly Dictionary<int, (int Mixer, int Channels)> _outputs = new();
    volatile int _musicDeviceMixer, _cartDeviceMixer, _previewDeviceMixer;
    volatile int _musicDeviceChannels = 2, _cartDeviceChannels = 2, _previewDeviceChannels = 2;
    volatile bool _previewOnAir;

    readonly HashSet<int> _initialised = new();
    readonly object _initLock = new();

    public AudioGraph(int? forcedDevice)
    {
        _forcedDevice = forcedDevice;
        EnsureInit(0); // the "no sound" device: lets decode channels exist before any card is opened
        Bass.Configure(Configuration.SplitBufferLength, SplitBufferMs);

        // PositionEx lets us ask "where was this source N bytes of output ago", i.e. what is audible right now.
        var busFlags = BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop | BassFlags.MixerPositionEx;
        MusicBus = Check(BassMix.CreateMixerStream(SampleRate, 2, busFlags), "music bus");
        CartBus = Check(BassMix.CreateMixerStream(SampleRate, 2, busFlags), "cart bus");
        Program = Check(BassMix.CreateMixerStream(SampleRate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop), "program mixer");
        PreviewBus = Check(BassMix.CreateMixerStream(SampleRate, 2, busFlags), "preview bus");

        _musicOut = Check(BassMix.CreateSplitStream(MusicBus, BassFlags.Decode, null), "music split");
        _cartOut = Check(BassMix.CreateSplitStream(CartBus, BassFlags.Decode, null), "cart split");
        _musicProg = Check(BassMix.CreateSplitStream(MusicBus, BassFlags.Decode, null), "music program split");
        _cartProg = Check(BassMix.CreateSplitStream(CartBus, BassFlags.Decode, null), "cart program split");
        BassMix.MixerAddChannel(Program, _musicProg, BassFlags.Default);
        BassMix.MixerAddChannel(Program, _cartProg, BassFlags.Default);
    }

    /// <summary>(Re)builds the device side of the graph. Safe to call while playing; there is a short gap on the outputs.</summary>
    public void Apply(AudioRouting routing)
    {
        foreach (var source in new[] { _musicOut, _cartOut, Program, PreviewBus })
            BassMix.MixerRemoveChannel(source);
        foreach (var (mixer, _) in _outputs.Values)
            Bass.StreamFree(mixer);
        _outputs.Clear();

        int musicDevice = Resolve(routing.Music.Device);
        int cartDevice = Resolve(routing.Carts.Device);
        int previewDevice = Resolve(routing.Preview.Device);

        var (musicMixer, musicChannels) = OutputFor(musicDevice);
        var (cartMixer, cartChannels) = OutputFor(cartDevice);
        var (previewMixer, previewChannels) = OutputFor(previewDevice);

        Attach(musicMixer, musicChannels, _musicOut, routing.Music.FirstChannel);
        Attach(cartMixer, cartChannels, _cartOut, routing.Carts.FirstChannel);
        Attach(previewMixer, previewChannels, PreviewBus, routing.Preview.FirstChannel);

        // Program rides along on the music card's mixer with an all-zero matrix: pulled in real time, never heard.
        BassMix.MixerAddChannel(musicMixer, Program, BassFlags.MixerChanMatrix);
        BassMix.ChannelSetMatrix(Program, new float[musicChannels, 2]);

        _musicDeviceMixer = musicMixer;
        _musicDeviceChannels = musicChannels;
        _cartDeviceMixer = cartMixer;
        _cartDeviceChannels = cartChannels;
        _previewDeviceMixer = previewMixer;
        _previewDeviceChannels = previewChannels;
        _previewOnAir = SamePair(previewDevice, routing.Preview, musicDevice, routing.Music, musicChannels)
                     || SamePair(previewDevice, routing.Preview, cartDevice, routing.Carts, cartChannels);
    }

    /// <summary>
    /// Opens only the Preview output (for a program that pre-listens but doesn't play on air, e.g. the playlist manager):
    /// the Music and Cartwall cards are left alone. <see cref="PreviewOnAir"/> still tells if Preview is one of their pairs.
    /// </summary>
    public void ApplyPreviewOnly(AudioRouting routing)
    {
        int previewDevice = Resolve(routing.Preview.Device);
        var (previewMixer, previewChannels) = OutputFor(previewDevice);
        Attach(previewMixer, previewChannels, PreviewBus, routing.Preview.FirstChannel);
        _previewDeviceMixer = previewMixer;
        _previewDeviceChannels = previewChannels;
        _previewOnAir = SamePair(previewDevice, routing.Preview, Resolve(routing.Music.Device), routing.Music, previewChannels)
                     || SamePair(previewDevice, routing.Preview, Resolve(routing.Carts.Device), routing.Carts, previewChannels);
    }

    static bool SamePair(int deviceA, OutputRoute a, int deviceB, OutputRoute b, int channels) =>
        deviceA == deviceB && Math.Clamp(a.FirstChannel & ~1, 0, channels - 2) == Math.Clamp(b.FirstChannel & ~1, 0, channels - 2);

    /// <summary>True when Preview comes out of the same card and pair as Music or Cartwall, i.e. it would be heard on air.</summary>
    public bool PreviewOnAir => _previewOnAir;

    /// <summary>Bytes (in bus format) still queued on the music card, for latency-corrected positions.</summary>
    public int MusicDelay => Delay(_musicDeviceMixer, _musicDeviceChannels);

    public int CartDelay => Delay(_cartDeviceMixer, _cartDeviceChannels);

    public int PreviewDelay => Delay(_previewDeviceMixer, _previewDeviceChannels);

    /// <summary>Peak levels of what is going out (music and carts combined, after ducking).</summary>
    public (float Left, float Right) Levels()
    {
        var m = new float[2];
        var c = new float[2];
        BassMix.ChannelGetLevel(_musicOut, m, 0.05f, LevelRetrievalFlags.Stereo);
        BassMix.ChannelGetLevel(_cartOut, c, 0.05f, LevelRetrievalFlags.Stereo);
        float duck = (float)Bass.ChannelGetAttribute(_musicOut, ChannelAttribute.Volume);
        return (Math.Min(1f, (m[0] + c[0]) * duck), Math.Min(1f, (m[1] + c[1]) * duck));
    }

    /// <summary>Fades music and carts, on the outputs and in the stream, to a level (1 = normal), e.g. under the microphone.</summary>
    public void Duck(float level, int milliseconds)
    {
        foreach (int split in new[] { _musicOut, _cartOut, _musicProg, _cartProg })
            Bass.ChannelSlideAttribute(split, ChannelAttribute.Volume, level, milliseconds);
    }

    // ───────────────────────── Devices ─────────────────────────

    /// <summary>Names of the enabled output devices (BASS device 1..n).</summary>
    public static IReadOnlyList<string> DeviceNames()
    {
        var list = new List<string>();
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
            if (info.IsEnabled) list.Add(info.Name);
        return list;
    }

    /// <summary>Number of output channels a device offers (initialises it if needed). At least 2.</summary>
    public int ChannelCount(string device) => ChannelCount(Resolve(device));

    public int Resolve(string device)
    {
        if (_forcedDevice is { } forced) return forced;
        int fallback = -1;
        for (int i = 1; Bass.GetDeviceInfo(i, out var info); i++)
        {
            if (!info.IsEnabled) continue;
            if (!string.IsNullOrEmpty(device) && info.Name == device) return i;
            if (info.IsDefault && fallback < 0) fallback = i;
        }
        return fallback < 0 ? 1 : fallback;
    }

    void EnsureInit(int device)
    {
        lock (_initLock)
        {
            if (!Bass.Init(device, SampleRate) && Bass.LastError != Errors.Already)
                throw new InvalidOperationException(L.F("Cannot open audio device {0}: {1}", device, Bass.LastError));
            _initialised.Add(device);
        }
    }

    /// <summary>Frees every device this graph opened (with <paramref name="keepNoSound"/>, not device 0, which others may use).</summary>
    public void Free(bool keepNoSound = false)
    {
        lock (_initLock)
        {
            foreach (int device in _initialised)
            {
                if (keepNoSound && device == 0) continue;
                Bass.CurrentDevice = device;
                Bass.Free();
            }
            _initialised.Clear();
        }
    }

    int ChannelCount(int device)
    {
        EnsureInit(device);
        Bass.CurrentDevice = device;
        return Math.Max(2, Bass.Info.SpeakerCount);
    }

    (int Mixer, int Channels) OutputFor(int device)
    {
        if (_outputs.TryGetValue(device, out var existing)) return existing;
        int channels = ChannelCount(device);
        Bass.CurrentDevice = device;
        int mixer = Check(BassMix.CreateMixerStream(SampleRate, channels, BassFlags.Float | BassFlags.MixerNonStop), $"output mixer on device {device}");
        Bass.ChannelPlay(mixer);
        return _outputs[device] = (mixer, channels);
    }

    static void Attach(int deviceMixer, int deviceChannels, int split, int firstChannel)
    {
        int first = Math.Clamp(firstChannel & ~1, 0, deviceChannels - 2);
        var matrix = new float[deviceChannels, 2];
        matrix[first, 0] = 1;
        matrix[first + 1, 1] = 1;
        // ChanBuffer keeps recent data so levels can be measured on the source.
        BassMix.MixerAddChannel(deviceMixer, split, BassFlags.MixerChanMatrix | BassFlags.MixerChanBuffer);
        BassMix.ChannelSetMatrix(split, matrix);
    }

    static int Delay(int deviceMixer, int deviceChannels)
    {
        if (deviceMixer == 0) return 0;
        int queued = Bass.ChannelGetData(deviceMixer, IntPtr.Zero, (int)DataFlags.Available);
        if (queued <= 0) return 0;
        int frames = queued / (deviceChannels * sizeof(float));
        return frames * 2 * sizeof(float);
    }

    // ───────────────────────── Test tone ─────────────────────────

    /// <summary>One second of 1 kHz at -12 dBFS on the given card and channel pair, independent of the buses.</summary>
    public void TestTone(OutputRoute route)
    {
        int device = Resolve(route.Device);
        int channels = ChannelCount(device);
        Bass.CurrentDevice = device;
        int first = Math.Clamp(route.FirstChannel & ~1, 0, channels - 2);

        int frames = SampleRate;
        var data = new float[frames * channels];
        for (int i = 0; i < frames; i++)
        {
            double env = Math.Min(1, Math.Min(i, frames - i) / (SampleRate * 0.02)); // 20 ms fades, no clicks
            float v = (float)(0.25 * env * Math.Sin(2 * Math.PI * 1000 * i / SampleRate));
            data[i * channels + first] = v;
            data[i * channels + first + 1] = v;
        }

        int h = Bass.CreateStream(SampleRate, channels, BassFlags.Float | BassFlags.AutoFree, StreamProcedureType.Push);
        if (h == 0) return;
        Bass.StreamPutData(h, data, (data.Length * sizeof(float)) | unchecked((int)0x80000000)); // last block: end of stream
        Bass.ChannelPlay(h);
    }

    static int Check(int handle, string what) =>
        handle != 0 ? handle : throw new InvalidOperationException(L.F("Could not create {0}: {1}", what, Bass.LastError));
}
