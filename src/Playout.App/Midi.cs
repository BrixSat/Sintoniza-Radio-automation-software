using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Playout.App;

/// <summary>A MIDI channel message: status (0x80–0xEF, its channel in the low four bits) and up to two data bytes.</summary>
public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public int Channel => (Status & 0x0F) + 1;
    public int Type => Status & 0xF0;
}

/// <summary>
/// Turns a MIDI byte stream into messages: running status, one or two data bytes, system exclusive and real-time
/// bytes (clock…) left out.
/// </summary>
public sealed class MidiParser
{
    byte _status;
    readonly byte[] _data = new byte[2];
    int _count;
    bool _sysex;

    public IEnumerable<MidiMessage> Feed(ReadOnlySpan<byte> bytes)
    {
        var messages = new List<MidiMessage>();
        foreach (byte b in bytes)
        {
            if (b >= 0xF8) continue; // real-time, anywhere
            if (b == 0xF0) { _sysex = true; continue; }
            if (b == 0xF7) { _sysex = false; continue; }
            if (b >= 0xF1) { _status = 0; _sysex = false; continue; } // system common: not for us, and it cancels running status
            if (b >= 0x80) { _status = b; _count = 0; _sysex = false; continue; }
            if (_sysex || _status == 0) continue;
            _data[_count++] = b;
            int needed = (_status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
            if (_count < needed) continue;
            messages.Add(new MidiMessage(_status, _data[0], needed == 2 ? _data[1] : (byte)0));
            _count = 0; // running status: the same status may go on
        }
        return messages;
    }
}

/// <summary>A MIDI input that is open: its messages arrive on a thread of its own.</summary>
public interface IMidiIn : IDisposable
{
    event Action<MidiMessage>? Message;
}

/// <summary>A MIDI output that is open.</summary>
public interface IMidiOut : IDisposable
{
    void Send(MidiMessage message);
}

/// <summary>This computer's MIDI ports: ALSA raw MIDI on Linux, the Windows MIDI API on Windows (macOS: not yet).</summary>
public static partial class MidiPorts
{
    public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    public static IReadOnlyList<string> Inputs() => OperatingSystem.IsWindows() ? WinNames(input: true) : OperatingSystem.IsLinux() ? LinuxNames() : [];
    public static IReadOnlyList<string> Outputs() => OperatingSystem.IsWindows() ? WinNames(input: false) : OperatingSystem.IsLinux() ? LinuxNames() : [];

    /// <summary>Opens an input by its name (on Linux also a path: /dev/snd/midiC1D0). Throws when it can't.</summary>
    public static IMidiIn OpenInput(string name) =>
        OperatingSystem.IsWindows() ? new WinIn(WinIndex(name, input: true))
        : OperatingSystem.IsLinux() ? new FileIn(LinuxPath(name))
        : throw new PlatformNotSupportedException(L.T("MIDI isn't available on this system yet"));

    public static IMidiOut OpenOutput(string name) =>
        OperatingSystem.IsWindows() ? new WinOut(WinIndex(name, input: false))
        : OperatingSystem.IsLinux() ? new FileOut(LinuxPath(name))
        : throw new PlatformNotSupportedException(L.T("MIDI isn't available on this system yet"));

    // ───────────────────────── Linux: ALSA raw MIDI (/dev/snd/midiC<card>D<device>) ─────────────────────────

    [GeneratedRegex(@"midiC(\d+)D(\d+)$")]
    private static partial Regex RawMidi();

    /// <summary>"Traktor Kontrol X1 (hw:1,0)" for each raw MIDI device.</summary>
    static List<string> LinuxNames()
    {
        var names = new List<string>();
        try
        {
            foreach (var path in Directory.GetFiles("/dev/snd", "midiC*D*").Order())
                if (RawMidi().Match(path) is { Success: true } m)
                    names.Add($"{CardName(m.Groups[1].Value)} (hw:{m.Groups[1].Value},{m.Groups[2].Value})");
        }
        catch { /* no sound devices */ }
        return names;
    }

    static string CardName(string card)
    {
        try
        {
            // /proc/asound/cards: " 1 [X1             ]: USB-Audio - Traktor Kontrol X1"
            foreach (var line in File.ReadLines("/proc/asound/cards"))
                if (line.TrimStart().StartsWith(card + " ") && line.IndexOf(" - ", StringComparison.Ordinal) is > 0 and var dash)
                    return line[(dash + 3)..].Trim();
        }
        catch { /* unknown */ }
        return L.F("Card {0}", card);
    }

    static string LinuxPath(string name)
    {
        if (name.StartsWith('/')) return name;
        var m = Regex.Match(name, @"\(hw:(\d+),(\d+)\)$");
        if (!m.Success) throw new IOException(L.F("No MIDI device {0}", name));
        return $"/dev/snd/midiC{m.Groups[1].Value}D{m.Groups[2].Value}";
    }

    sealed class FileIn : IMidiIn
    {
        readonly FileStream _stream;
        readonly Thread _thread;
        volatile bool _closed;
        public event Action<MidiMessage>? Message;

        public FileIn(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
            _thread = new Thread(Run) { IsBackground = true, Name = "MIDI in" };
            _thread.Start();
        }

        void Run()
        {
            var parser = new MidiParser();
            var buffer = new byte[256];
            while (!_closed)
            {
                int n;
                try { n = _stream.Read(buffer, 0, buffer.Length); }
                catch { return; }
                if (n <= 0) { Thread.Sleep(50); continue; } // a pipe with no writer yet (tests)
                foreach (var message in parser.Feed(buffer.AsSpan(0, n))) Message?.Invoke(message);
            }
        }

        public void Dispose()
        {
            _closed = true;
            _stream.Dispose(); // ends the read
        }
    }

    sealed class FileOut(string path) : IMidiOut
    {
        readonly FileStream _stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1);

        public void Send(MidiMessage m)
        {
            int length = m.Type is 0xC0 or 0xD0 ? 2 : 3;
            _stream.Write([m.Status, m.Data1, m.Data2], 0, length);
            _stream.Flush();
        }

        public void Dispose() => _stream.Dispose();
    }

    // ───────────────────────── Windows: winmm ─────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MidiCaps
    {
        public ushort Mid, Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support; // input caps: dwSupport; output caps have more fields, read separately
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MidiOutCaps
    {
        public ushort Mid, Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology, Voices, Notes, ChannelMask;
        public uint Support;
    }

    delegate void MidiInProc(IntPtr handle, uint msg, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint midiInGetDevCapsW(UIntPtr id, out MidiCaps caps, uint size);
    [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr handle, uint id, MidiInProc callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint midiOutGetDevCapsW(UIntPtr id, out MidiOutCaps caps, uint size);
    [DllImport("winmm.dll")] static extern uint midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] static extern uint midiOutClose(IntPtr handle);

    const uint CallbackFunction = 0x30000, MimData = 0x3C3;

    static List<string> WinNames(bool input)
    {
        var names = new List<string>();
        uint count = input ? midiInGetNumDevs() : midiOutGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            if (input && midiInGetDevCapsW((UIntPtr)i, out var ic, (uint)Marshal.SizeOf<MidiCaps>()) == 0) names.Add(ic.Name);
            else if (!input && midiOutGetDevCapsW((UIntPtr)i, out var oc, (uint)Marshal.SizeOf<MidiOutCaps>()) == 0) names.Add(oc.Name);
        }
        return names;
    }

    static uint WinIndex(string name, bool input)
    {
        int index = WinNames(input).IndexOf(name);
        return index >= 0 ? (uint)index : throw new IOException(L.F("No MIDI device {0}", name));
    }

    sealed class WinIn : IMidiIn
    {
        readonly IntPtr _handle;
        readonly MidiInProc _proc; // strong reference: Windows holds a raw function pointer
        public event Action<MidiMessage>? Message;

        public WinIn(uint id)
        {
            _proc = (_, msg, _, p1, _) =>
            {
                if (msg != MimData) return;
                uint data = (uint)p1.ToInt64();
                Message?.Invoke(new MidiMessage((byte)data, (byte)(data >> 8), (byte)(data >> 16)));
            };
            if (midiInOpen(out _handle, id, _proc, IntPtr.Zero, CallbackFunction) != 0) throw new IOException(L.T("The MIDI input can't be opened"));
            midiInStart(_handle);
        }

        public void Dispose()
        {
            midiInStop(_handle);
            midiInClose(_handle);
        }
    }

    sealed class WinOut : IMidiOut
    {
        readonly IntPtr _handle;

        public WinOut(uint id)
        {
            if (midiOutOpen(out _handle, id, IntPtr.Zero, IntPtr.Zero, 0) != 0) throw new IOException(L.T("The MIDI output can't be opened"));
        }

        public void Send(MidiMessage m) => midiOutShortMsg(_handle, (uint)(m.Status | m.Data1 << 8 | m.Data2 << 16));
        public void Dispose() => midiOutClose(_handle);
    }
}
