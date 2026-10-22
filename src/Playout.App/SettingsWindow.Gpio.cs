using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Playout.App;

/// <summary>Settings → GPIO: the serial port, what each input does and what each output shows.</summary>
public partial class SettingsWindow
{
    CheckBox? _gpioBox;
    ComboBox? _gpioPortBox;
    readonly List<(ComboBox OnClose, ComboBox OnOpen, NumericUpDown Cart)> _gpioInputs = new();
    readonly List<ComboBox> _gpioOutputs = new();
    TextBlock? _gpioTest;

    static readonly GpioAction[] GpioActions = Enum.GetValues<GpioAction>();
    static readonly GpioSignal[] GpioSignals = Enum.GetValues<GpioSignal>();

    static string ActionName(GpioAction a) => a switch
    {
        GpioAction.Play => L.T("Play (fader start)"),
        GpioAction.Next => L.T("Next"),
        GpioAction.Stop => L.T("Stop"),
        GpioAction.PlayPause => L.T("Pause / carry on"),
        GpioAction.Cart => L.T("Cart"),
        _ => L.T("Nothing"),
    };

    static string SignalName(GpioSignal s) => s switch
    {
        GpioSignal.OnAir => L.T("On air (something playing)"),
        GpioSignal.Advert => L.T("An advert on air"),
        GpioSignal.Live => L.T("A live input or stream on air"),
        GpioSignal.Silence => L.T("Silence (dead air)"),
        _ => L.T("Not used (held on: the supply for the inputs)"),
    };

    void BuildGpio(GpioSettings g)
    {
        GpioPanel.Children.Clear();
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
