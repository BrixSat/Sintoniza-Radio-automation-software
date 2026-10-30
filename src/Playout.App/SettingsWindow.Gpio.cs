using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Playout.App;

/// <summary>Settings → GPIO: the serial port, the network and MIDI — what each input does and what each output shows.</summary>
public partial class SettingsWindow
{
    CheckBox? _gpioBox;
    ComboBox? _gpioPortBox;
    readonly List<(ComboBox OnClose, ComboBox OnOpen, NumericUpDown Cart)> _gpioInputs = new();
    readonly List<ComboBox> _gpioOutputs = new();
    TextBlock? _gpioTest;
    /// <summary>The network and MIDI settings being edited (their rows change them in place).</summary>
    GpioSettings _gpioEdit = new();

    static readonly GpioAction[] GpioActions = Enum.GetValues<GpioAction>();
    static readonly GpioSignal[] GpioSignals = Enum.GetValues<GpioSignal>();

    static string ActionName(GpioAction a) => a switch
    {
        GpioAction.Play => L.T("Play (fader start)"),
        GpioAction.Next => L.T("Next"),
        GpioAction.Stop => L.T("Stop"),
        GpioAction.PlayPause => L.T("Pause / carry on"),
        GpioAction.Cart => L.T("Cart"),
        GpioAction.MicLive => L.T("Mic fader up (MIC LIVE)"),
        GpioAction.MicOff => L.T("Mic fader down"),
        _ => L.T("Nothing"),
    };

    static string SignalName(GpioSignal s) => s switch
    {
        GpioSignal.OnAir => L.T("On air (something playing)"),
        GpioSignal.Advert => L.T("An advert on air"),
        GpioSignal.Live => L.T("A live input or stream on air"),
        GpioSignal.Silence => L.T("Silence (dead air)"),
        GpioSignal.MicLive => L.T("The ON AIR sign (mic live, live show or input)"),
        _ => L.T("Not used (held on: the supply for the inputs)"),
    };

    void BuildGpio(GpioSettings g)
    {
        _gpioEdit = g.Clone();
        GpioPanel.Children.Clear();
        GpioPanel.Children.Add(Heading(L.T("SERIAL PORT")));
        _gpioInputs.Clear();
        _gpioOutputs.Clear();
        _gpioBox = new CheckBox { Content = L.T("Use GPIO on a serial port: the desk's faders and buttons start the playout, and lights show what is on air"), IsChecked = g.Enabled, FontWeight = FontWeight.SemiBold };
        GpioPanel.Children.Add(_gpioBox);

        var ports = SafePorts();
        if (g.Port.Length > 0 && !ports.Contains(g.Port)) ports.Insert(0, g.Port);
        _gpioPortBox = new ComboBox { ItemsSource = ports, SelectedItem = g.Port.Length > 0 ? g.Port : ports.FirstOrDefault(), MinWidth = 220 };
        GpioPanel.Children.Add(Row(L.T("Serial port"), _gpioPortBox));

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("Inputs: a contact between the line and DTR or RTS (DB-9 pins)"), Margin = new Avalonia.Thickness(0, 6, 0, 0) });
        foreach (var (name, input) in new[] { ("CTS (8)", g.Cts), ("DSR (6)", g.Dsr), ("DCD (1)", g.Dcd) })
        {
            var onClose = new ComboBox { ItemsSource = GpioActions.Select(ActionName).ToList(), SelectedIndex = Array.IndexOf(GpioActions, input.OnClose), MinWidth = 170 };
            var onOpen = new ComboBox { ItemsSource = GpioActions.Select(ActionName).ToList(), SelectedIndex = Array.IndexOf(GpioActions, input.OnOpen), MinWidth = 170 };
            var cart = new NumericUpDown { Minimum = 1, Maximum = 48, Value = Math.Clamp(input.Cart, 1, 48), FormatString = "0", Width = 110 };
            ToolTip.SetTip(cart, L.T("For Cart: which cart of the cartwall on screen (1 = the first)"));
            _gpioInputs.Add((onClose, onOpen, cart));
            var line = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            line.Children.Add(new TextBlock { Text = name, Width = 70, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
            line.Children.Add(new TextBlock { Text = L.T("closes:"), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(onClose);
            line.Children.Add(new TextBlock { Text = L.T("opens:"), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(onOpen);
            line.Children.Add(new TextBlock { Text = L.T("cart"), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(cart);
            GpioPanel.Children.Add(line);
        }

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("Outputs: on while…"), Margin = new Avalonia.Thickness(0, 6, 0, 0) });
        foreach (var (name, signal) in new[] { ("DTR (4)", g.Dtr), ("RTS (7)", g.Rts) })
        {
            var box = new ComboBox { ItemsSource = GpioSignals.Select(SignalName).ToList(), SelectedIndex = Array.IndexOf(GpioSignals, signal), MinWidth = 300 };
            _gpioOutputs.Add(box);
            GpioPanel.Children.Add(Row(name, box));
        }

        var test = new Button { Content = L.T("Test the inputs") };
        _gpioTest = new TextBlock { Classes = { "note" }, VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) => await TestGpio();
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        testRow.Children.Add(test);
        testRow.Children.Add(_gpioTest);
        GpioPanel.Children.Add(testRow);
        GpioPanel.Children.Add(new TextBlock
        {
            Classes = { "note" },
            Text = L.T("Fader start: wire the fader's contact to CTS and choose Play on close; when it opens again (fader down), Next or nothing. A short press counts once (a contact that bounces is ignored). Next and Stop aren't done while an advert is on air, as on screen. A USB serial adapter works; the port must not be used by anything else (e.g. RDS)."),
        });
        BuildNetworkGpio(_gpioEdit.Network);
        BuildMidiGpio(_gpioEdit.Midi);
    }

    static TextBlock Heading(string text) => new() { Classes = { "label" }, Text = text, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 14, 0, 0) };

    static ComboBox Choice<T>(T[] values, Func<T, string> name, T value, Action<T> set, double width = 170)
    {
        var box = new ComboBox { ItemsSource = values.Select(name).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(values, value)), MinWidth = width };
        box.SelectionChanged += (_, _) => set(values[Math.Max(0, box.SelectedIndex)]);
        return box;
    }

    static NumericUpDown Number(int min, int max, int value, Action<int> set, double width = 100)
    {
        var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), FormatString = "0", Width = width };
        box.ValueChanged += (_, _) => set((int)(box.Value ?? min));
        return box;
    }

    static TextBox Text(string value, string placeholder, Action<string> set, double width = 180)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder, Width = width };
        box.TextChanged += (_, _) => set(box.Text ?? "");
        return box;
    }

    static WrapPanel Line(params Control[] controls)
    {
        var line = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var c in controls)
        {
            if (c is TextBlock t) t.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(c);
        }
        return line;
    }

    static TextBlock Label(string text) => new() { Text = text };

    /// <summary>A list of rows, each with ✕, and "+ Add".</summary>
    static StackPanel Rows<T>(List<T> items, Func<T, Control[]> row, Func<T> add, string addText)
    {
        var panel = new StackPanel { Spacing = 6 };
        void Show()
        {
            panel.Children.Clear();
            foreach (var item in items.ToList())
            {
                var remove = new Button { Content = "✕", Padding = new Avalonia.Thickness(8, 2) };
                remove.Click += (_, _) => { items.Remove(item); Show(); };
                panel.Children.Add(Line([.. row(item), remove]));
            }
            var plus = new Button { Content = addText, HorizontalAlignment = HorizontalAlignment.Left };
            plus.Click += (_, _) => { items.Add(add()); Show(); };
            panel.Children.Add(plus);
        }
        Show();
        panel.Tag = (Action)Show;
        return panel;
    }

    // ───────────────────────── Network ─────────────────────────

    void BuildNetworkGpio(NetGpioSettings n)
    {
        GpioPanel.Children.Add(Heading(L.T("NETWORK")));
        var on = new CheckBox { Content = L.T("Network GPIO: texts that arrive (TCP or UDP) start the playout, and texts go out when what is on air changes"), IsChecked = n.Enabled };
        on.IsCheckedChanged += (_, _) => n.Enabled = on.IsChecked == true;
        GpioPanel.Children.Add(on);
        GpioPanel.Children.Add(Line(Label(L.T("Port (TCP and UDP)")), Number(1, 65535, n.Port, v => n.Port = v, 130),
            Label(L.T("Only from")), Text(n.AllowFrom, L.T("192.168.1.20, … (empty: any)"), v => n.AllowFrom = v, 240)));
        GpioPanel.Children.Add(Line(Label(L.T("Outputs also by UDP to")), Text(n.SendTo, "192.168.1.30:9101", v => n.SendTo = v, 200)));

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("When this text arrives…") });
        var inputs = Rows(n.Inputs, i => [
            Text(i.Text, "FADER1_ON", v => i.Text = v),
            Choice(GpioActions, ActionName, i.Action, v => i.Action = v),
            Label(L.T("cart")), Number(1, 48, i.Cart, v => i.Cart = v),
        ], () => new NetGpioInput { Action = GpioAction.Play }, L.T("+ Add a text"));
        GpioPanel.Children.Add(inputs);

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("Send when it starts / when it ends") });
        GpioPanel.Children.Add(Rows(n.Outputs, o => [
            Choice(GpioSignals[1..], SignalName, o.Signal, v => o.Signal = v, 230),
            Text(o.OnText, "ONAIR_ON", v => o.OnText = v, 150),
            Text(o.OffText, "ONAIR_OFF", v => o.OffText = v, 150),
        ], () => new NetGpioOutput { Signal = GpioSignal.OnAir }, L.T("+ Add an output")));

        // Listen: what arrives on the port, to add it without typing it.
        var heard = new TextBlock { Classes = { "note" }, VerticalAlignment = VerticalAlignment.Center };
        var listen = new ToggleButton { Content = L.T("Listen") };
        var addIt = new Button { Content = L.T("Add the last received"), IsEnabled = false };
        string? last = null;
        NetGpio? listener = null;
        listen.IsCheckedChanged += (_, _) =>
        {
            listener?.Dispose();
            listener = null;
            if (listen.IsChecked != true) { GpioTestRequested?.Invoke(false); heard.Text = ""; return; }
            GpioTestRequested?.Invoke(true);
            try
            {
                listener = new NetGpio(new NetGpioSettings { Port = n.Port, AllowFrom = n.AllowFrom });
                heard.Text = L.F("Listening on port {0}…", n.Port);
                listener.Received += (text, from) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    last = text;
                    heard.Text = L.F("Received \"{0}\" from {1}", text, from);
                    addIt.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                heard.Text = L.F("The port can't be used: {0}", ex.Message);
                GpioTestRequested?.Invoke(false);
            }
        };
        addIt.Click += (_, _) =>
        {
            if (last == null) return;
            n.Inputs.Add(new NetGpioInput { Text = last, Action = GpioAction.Play });
            (inputs.Tag as Action)?.Invoke();
        };
        Closed += (_, _) => listener?.Dispose();
        GpioPanel.Children.Add(Line(listen, addIt, heard));
        GpioPanel.Children.Add(new TextBlock
        {
            Classes = { "note" },
            Text = L.T("A GPIO-to-network box or a desk sends a text (a line by TCP, or a UDP message) for each contact: set what each does. Outputs go to every TCP connection and, if set, by UDP. Anyone who can reach the port can press Play: fill in Only from, and keep it to the station's network."),
        });
    }

    // ───────────────────────── MIDI ─────────────────────────

    static string KindName(MidiKind k) => k == MidiKind.Control ? L.T("Controller (CC)") : L.T("Note");

    void BuildMidiGpio(MidiGpioSettings m)
    {
        GpioPanel.Children.Add(Heading("MIDI"));
        if (!MidiPorts.Supported)
        {
            GpioPanel.Children.Add(new TextBlock { Classes = { "note" }, Text = L.T("MIDI isn't available on this system yet (Linux and Windows only).") });
            return;
        }
        var on = new CheckBox { Content = L.T("MIDI GPIO: a desk or controller's notes and faders start the playout, and its lights show what is on air"), IsChecked = m.Enabled };
        on.IsCheckedChanged += (_, _) => m.Enabled = on.IsChecked == true;
        GpioPanel.Children.Add(on);
        string none = L.T("(none)");
        IReadOnlyList<string> ins = [], outs = [];
        try { (ins, outs) = (MidiPorts.Inputs(), MidiPorts.Outputs()); }
        catch { /* no MIDI */ }
        ComboBox Port(IReadOnlyList<string> ports, string value, Action<string> set)
        {
            var list = new List<string> { none };
            list.AddRange(ports);
            if (value.Length > 0 && !list.Contains(value)) list.Add(value);
            var box = new ComboBox { ItemsSource = list, SelectedItem = value.Length > 0 ? value : none, MinWidth = 260 };
            box.SelectionChanged += (_, _) => set(box.SelectedItem as string is { } v && v != none ? v : "");
            return box;
        }
        GpioPanel.Children.Add(Line(Label(L.T("Input")), Port(ins, m.Input, v => m.Input = v), Label(L.T("Output (lights)")), Port(outs, m.Output, v => m.Output = v)));

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("Notes and controllers: channel (0 = any), number, when pressed / when released") });
        var inputs = Rows(m.Inputs, i => [
            Label(L.T("ch.")), Number(0, 16, i.Channel, v => i.Channel = v, 90),
            Choice(Enum.GetValues<MidiKind>(), KindName, i.Kind, v => i.Kind = v, 140),
            Number(0, 127, i.Number, v => i.Number = v, 100),
            Choice(GpioActions, ActionName, i.OnPress, v => i.OnPress = v, 150),
            Choice(GpioActions, ActionName, i.OnRelease, v => i.OnRelease = v, 150),
            Label(L.T("cart")), Number(1, 48, i.Cart, v => i.Cart = v, 90),
        ], () => new MidiGpioInput { Kind = MidiKind.Note, OnPress = GpioAction.Play }, L.T("+ Add a note or controller"));
        GpioPanel.Children.Add(inputs);

        GpioPanel.Children.Add(new TextBlock { Classes = { "label" }, Text = L.T("Lights: sent on (127) when it starts, off when it ends") });
        GpioPanel.Children.Add(Rows(m.Outputs, o => [
            Choice(GpioSignals[1..], SignalName, o.Signal, v => o.Signal = v, 230),
            Label(L.T("ch.")), Number(1, 16, o.Channel, v => o.Channel = v, 90),
            Choice(Enum.GetValues<MidiKind>(), KindName, o.Kind, v => o.Kind = v, 140),
            Number(0, 127, o.Number, v => o.Number = v, 100),
        ], () => new MidiGpioOutput { Signal = GpioSignal.OnAir }, L.T("+ Add a light")));

        var heard = new TextBlock { Classes = { "note" }, VerticalAlignment = VerticalAlignment.Center };
        var listen = new ToggleButton { Content = L.T("Listen") };
        var addIt = new Button { Content = L.T("Add the last received"), IsEnabled = false };
        (MidiKind Kind, int Channel, int Number)? last = null;
        MidiGpio? listener = null;
        listen.IsCheckedChanged += (_, _) =>
        {
            listener?.Dispose();
            listener = null;
            if (listen.IsChecked != true) { GpioTestRequested?.Invoke(false); heard.Text = ""; return; }
            if (m.Input.Length == 0) { heard.Text = L.T("Choose the input first."); listen.IsChecked = false; return; }
            GpioTestRequested?.Invoke(true);
            try
            {
                listener = new MidiGpio(new MidiGpioSettings { Input = m.Input });
                heard.Text = L.T("Listening: press a button or move a fader…");
                listener.Received += (kind, channel, number, pressed) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    last = (kind, channel, number);
                    heard.Text = L.F("{0} {1} · channel {2} · {3}", KindName(kind), number, channel, pressed ? L.T("pressed") : L.T("released"));
                    addIt.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                heard.Text = L.F("The MIDI input can't be opened: {0}", ex.Message);
                GpioTestRequested?.Invoke(false);
            }
        };
        addIt.Click += (_, _) =>
        {
            if (last is not { } l) return;
            m.Inputs.Add(new MidiGpioInput { Kind = l.Kind, Channel = l.Channel, Number = l.Number, OnPress = GpioAction.Play });
            (inputs.Tag as Action)?.Invoke();
        };
        Closed += (_, _) => listener?.Dispose();
        GpioPanel.Children.Add(Line(listen, addIt, heard));
        GpioPanel.Children.Add(new TextBlock
        {
            Classes = { "note" },
            Text = L.T("Listen, then press the button or move the fader: Add the last received puts it in the list, set to Play; choose what it does. A fader (controller) counts as pressed from the middle up, once each way. A controller in its own mode (e.g. a DJ controller's) may need its MIDI mode switched on."),
        });
    }

    static List<string> SafePorts()
    {
        try { return SerialPort.GetPortNames().Order().ToList(); }
        catch { return new(); }
    }

    static Control Row(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    GpioSettings GpioFromUi()
    {
        GpioInput Input(int i) => new()
        {
            OnClose = GpioActions[Math.Max(0, _gpioInputs[i].OnClose.SelectedIndex)],
            OnOpen = GpioActions[Math.Max(0, _gpioInputs[i].OnOpen.SelectedIndex)],
            Cart = (int)(_gpioInputs[i].Cart.Value ?? 1),
        };
        return new GpioSettings
        {
            Enabled = _gpioBox?.IsChecked == true,
            Port = _gpioPortBox?.SelectedItem as string ?? "",
            Cts = Input(0), Dsr = Input(1), Dcd = Input(2),
            Dtr = GpioSignals[Math.Max(0, _gpioOutputs[0].SelectedIndex)],
            Rts = GpioSignals[Math.Max(0, _gpioOutputs[1].SelectedIndex)],
            Network = _gpioEdit.Network,
            Midi = _gpioEdit.Midi,
        };
    }

    /// <summary>Opens the port for five seconds and shows the inputs as they change (the playout's own use of it pauses meanwhile).</summary>
    async Task TestGpio()
    {
        if (_gpioTest == null) return;
        var settings = GpioFromUi();
        if (settings.Port.Length == 0) { _gpioTest.Text = L.T("Choose the serial port."); return; }
        GpioTestRequested?.Invoke(true);
        try
        {
            using var gpio = await Task.Run(() => new GpioService(new SerialPins(settings.Port), settings, poll: false));
            for (int i = 0; i < 50; i++)
            {
                gpio.Poll();
                var (cts, dsr, dcd) = gpio.Inputs;
                string State(bool on) => on ? L.T("closed") : L.T("open");
                _gpioTest.Text = L.F("CTS {0} · DSR {1} · DCD {2}", State(cts), State(dsr), State(dcd));
                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            _gpioTest.Text = L.F("The port can't be opened: {0}", ex.Message);
        }
        finally
        {
            GpioTestRequested?.Invoke(false);
        }
    }

    /// <summary>The test wants the port (true) or is done with it (false): the playout lets go of it meanwhile.</summary>
    public event Action<bool>? GpioTestRequested;
}
