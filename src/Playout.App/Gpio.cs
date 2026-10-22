using System.IO.Ports;

namespace Playout.App;

/// <summary>What a GPIO input does when its contact closes (or opens).</summary>
public enum GpioAction
{
    None,
    /// <summary>Play (fader start: the desk's fader opened).</summary>
    Play,
    /// <summary>Crossfade to the next item now.</summary>
    Next,
    Stop,
    /// <summary>Pause, or carry on if paused.</summary>
    PlayPause,
    /// <summary>Fire a cart of the cartwall on screen (<see cref="GpioInput.Cart"/>, 1 = the first).</summary>
    Cart,
}

/// <summary>What a GPIO output shows (its contact is closed while it is so).</summary>
public enum GpioSignal
{
    None,
    /// <summary>Something is playing (an ON AIR light).</summary>
    OnAir,
    /// <summary>An advert is on air.</summary>
    Advert,
    /// <summary>A live input or internet stream is on air.</summary>
    Live,
    /// <summary>Dead air: nothing is heard although something is playing.</summary>
    Silence,
}

/// <summary>An input line and what it does.</summary>
public sealed class GpioInput
{
    public GpioAction OnClose { get; set; }
    public GpioAction OnOpen { get; set; }
    /// <summary>For <see cref="GpioAction.Cart"/>: which cart (1 = the first on the page).</summary>
    public int Cart { get; set; } = 1;
}

/// <summary>
/// GPIO through a serial port's control lines (this computer's): CTS, DSR and DCD are inputs (a contact between the
/// line and DTR or RTS, which are held on), DTR and RTS are outputs. Desks, relay boxes and USB adapters do it so.
/// </summary>
public sealed class GpioSettings
{
    public bool Enabled { get; set; }
    /// <summary>COM3, /dev/ttyUSB0…</summary>
    public string Port { get; set; } = "";
    public GpioInput Cts { get; set; } = new() { OnClose = GpioAction.Play };
    public GpioInput Dsr { get; set; } = new();
    public GpioInput Dcd { get; set; } = new();
    /// <summary>An output not used (<see cref="GpioSignal.None"/>) is held on: the supply of the input contacts.</summary>
    public GpioSignal Dtr { get; set; } = GpioSignal.None;
    public GpioSignal Rts { get; set; } = GpioSignal.None;

    public GpioSettings Clone() => new()
    {
        Enabled = Enabled, Port = Port, Dtr = Dtr, Rts = Rts,
        Cts = new() { OnClose = Cts.OnClose, OnOpen = Cts.OnOpen, Cart = Cts.Cart },
        Dsr = new() { OnClose = Dsr.OnClose, OnOpen = Dsr.OnOpen, Cart = Dsr.Cart },
        Dcd = new() { OnClose = Dcd.OnClose, OnOpen = Dcd.OnOpen, Cart = Dcd.Cart },
    };
}

/// <summary>The lines of a GPIO port (a serial port, or a stand-in for testing).</summary>
public interface IGpioPins : IDisposable
{
    bool Cts { get; }
    bool Dsr { get; }
    bool Dcd { get; }
    bool Dtr { set; }
    bool Rts { set; }
}

/// <summary>A serial port's control lines.</summary>
sealed class SerialPins : IGpioPins
{
    readonly SerialPort _port;

    public SerialPins(string name)
    {
        _port = new SerialPort(name) { Handshake = Handshake.None };
        _port.Open();
    }

    public bool Cts => _port.CtsHolding;
    public bool Dsr => _port.DsrHolding;
    public bool Dcd => _port.CDHolding;
    public bool Dtr { set => _port.DtrEnable = value; }
    public bool Rts { set => _port.RtsEnable = value; }

    public void Dispose() => _port.Dispose();
}

/// <summary>
/// Watches the GPIO inputs (every 20 ms; a change must hold for two looks, so a bouncing contact counts once) and sets
/// the outputs. <see cref="Poll"/> runs on a timer of its own; <see cref="Fired"/> is raised from it.
/// </summary>
public sealed class GpioService : IDisposable
{
    readonly IGpioPins _pins;
    readonly GpioSettings _s;
    readonly Timer? _timer;
    readonly bool[] _state = new bool[3], _seen = new bool[3];
    readonly int[] _held = new int[3];
    bool _started;

    /// <summary>An input changed: the action and, for a cart, which (1 = first). Raised off the UI thread.</summary>
    public event Action<GpioAction, int>? Fired;

    /// <param name="pins">The port's lines; <paramref name="poll"/> false: <see cref="Poll"/> is called by hand (tests).</param>
    public GpioService(IGpioPins pins, GpioSettings settings, bool poll = true)
    {
        _pins = pins;
        _s = settings.Clone();
        Show(false, false, false, false);
        if (poll) _timer = new Timer(_ => Poll(), null, 0, 20);
    }

    /// <summary>Opens the settings' serial port. Throws when it can't be opened.</summary>
    public static GpioService Open(GpioSettings settings) => new(new SerialPins(settings.Port), settings);

    /// <summary>The inputs as last seen (closed = true): CTS, DSR, DCD.</summary>
    public (bool Cts, bool Dsr, bool Dcd) Inputs => (_state[0], _state[1], _state[2]);

    public void Poll()
    {
        bool[] now;
        try { now = [_pins.Cts, _pins.Dsr, _pins.Dcd]; }
        catch { return; } // the port went away: nothing changes
        var inputs = new[] { _s.Cts, _s.Dsr, _s.Dcd };
        lock (_state)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!_started) { _state[i] = _seen[i] = now[i]; continue; } // as it is at the start: not a change
                if (now[i] != _seen[i]) { _seen[i] = now[i]; _held[i] = 1; continue; }
                if (now[i] == _state[i] || ++_held[i] < 2) continue;
                _state[i] = now[i];
                var action = now[i] ? inputs[i].OnClose : inputs[i].OnOpen;
                if (action != GpioAction.None) Fired?.Invoke(action, inputs[i].Cart);
            }
            _started = true;
        }
    }

    /// <summary>Sets the outputs from what is on air now.</summary>
    public void Show(bool playing, bool advert, bool live, bool silence)
    {
        bool On(GpioSignal signal) => signal switch
        {
            GpioSignal.OnAir => playing,
            GpioSignal.Advert => advert,
            GpioSignal.Live => live,
            GpioSignal.Silence => silence,
            _ => true, // not used as an output: held on, as the supply of the input contacts
        };
        try
        {
            _pins.Dtr = On(_s.Dtr);
            _pins.Rts = On(_s.Rts);
        }
        catch { /* the port went away */ }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _pins.Dispose();
    }
}
