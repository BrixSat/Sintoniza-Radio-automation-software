namespace Playout.App;

public enum MidiKind
{
    /// <summary>A note: on = pressed, off (or on with velocity 0) = released.</summary>
    Note,
    /// <summary>A controller (CC): 64 or more = pressed (a fader up), less = released.</summary>
    Control,
}

/// <summary>A note or controller that does something when pressed and when released.</summary>
public sealed class MidiGpioInput
{
    /// <summary>1–16; 0 = any channel.</summary>
    public int Channel { get; set; }
    public MidiKind Kind { get; set; }
    /// <summary>The note or controller number (0–127).</summary>
    public int Number { get; set; }
    public GpioAction OnPress { get; set; }
    public GpioAction OnRelease { get; set; }
    /// <summary>For <see cref="GpioAction.Cart"/>: which cart (1 = the first on the page).</summary>
    public int Cart { get; set; } = 1;
}

/// <summary>A note or controller sent when a signal starts (127) and ends (0): a light on the controller.</summary>
public sealed class MidiGpioOutput
{
    public GpioSignal Signal { get; set; }
    public int Channel { get; set; } = 1;
    public MidiKind Kind { get; set; }
    public int Number { get; set; }
}

/// <summary>GPIO from a MIDI desk or controller (this computer's).</summary>
public sealed class MidiGpioSettings
{
    public bool Enabled { get; set; }
    /// <summary>The input's name as listed (empty: none).</summary>
    public string Input { get; set; } = "";
    /// <summary>The output's name, for the lights (empty: none).</summary>
    public string Output { get; set; } = "";
    public List<MidiGpioInput> Inputs { get; set; } = new();
    public List<MidiGpioOutput> Outputs { get; set; } = new();
}

/// <summary>MIDI GPIO at work: notes and controllers fire actions; signals light the controller.</summary>
public sealed class MidiGpio : IGpioSource
{
    readonly MidiGpioSettings _s;
    readonly IMidiIn? _in;
    readonly IMidiOut? _out;
    readonly Dictionary<(int Channel, int Number), bool> _controls = new();
    readonly Dictionary<GpioSignal, bool> _shown = new();

    public event Action<GpioAction, int>? Fired;
    /// <summary>Each note or controller that arrives: kind, channel, number, pressed (for Settings → Listen). Raised off the UI thread.</summary>
    public event Action<MidiKind, int, int, bool>? Received;

    /// <summary>With ports already open (tests), or null to open the settings' ones. Throws when they can't be opened.</summary>
    public MidiGpio(MidiGpioSettings settings, IMidiIn? input = null, IMidiOut? output = null)
    {
        _s = settings;
        _in = input ?? (settings.Input.Length > 0 ? MidiPorts.OpenInput(settings.Input) : null);
        try { _out = output ?? (settings.Output.Length > 0 ? MidiPorts.OpenOutput(settings.Output) : null); }
        catch { _in?.Dispose(); throw; }
        if (_in != null) _in.Message += Take;
    }

    void Take(MidiMessage m)
    {
        MidiKind kind;
        bool pressed;
        switch (m.Type)
        {
            case 0x90:
                (kind, pressed) = (MidiKind.Note, m.Data2 > 0);
                break;
            case 0x80:
                (kind, pressed) = (MidiKind.Note, false);
                break;
            case 0xB0:
                kind = MidiKind.Control;
                pressed = m.Data2 >= 64;
                // A fader sends many values: only crossing the middle counts.
                lock (_controls)
                {
                    bool known = _controls.TryGetValue((m.Channel, m.Data1), out bool was);
                    _controls[(m.Channel, m.Data1)] = pressed;
                    // Its first value only says where it is, unless that is up (a button that sends 127).
                    if (known ? was == pressed : !pressed) return;
                }
                break;
            default:
                return;
        }
        Received?.Invoke(kind, m.Channel, m.Data1, pressed);
        foreach (var input in _s.Inputs)
        {
            if (input.Kind != kind || input.Number != m.Data1 || (input.Channel != 0 && input.Channel != m.Channel)) continue;
            var action = pressed ? input.OnPress : input.OnRelease;
            if (action != GpioAction.None) Fired?.Invoke(action, input.Cart);
        }
    }

    public void Show(GpioState state)
    {
        if (_out == null) return;
        foreach (var output in _s.Outputs)
        {
            bool on = state.Is(output.Signal);
            if (_shown.TryGetValue(output.Signal, out bool was) && was == on) continue;
            _shown[output.Signal] = on;
            byte channel = (byte)(Math.Clamp(output.Channel, 1, 16) - 1);
            byte number = (byte)Math.Clamp(output.Number, 0, 127);
            var message = output.Kind == MidiKind.Control
                ? new MidiMessage((byte)(0xB0 | channel), number, (byte)(on ? 127 : 0))
                : on ? new MidiMessage((byte)(0x90 | channel), number, 127) : new MidiMessage((byte)(0x80 | channel), number, 0);
            try { _out.Send(message); }
            catch { /* the device went away */ }
        }
    }

    public void Dispose()
    {
        _in?.Dispose();
        _out?.Dispose();
    }
}
