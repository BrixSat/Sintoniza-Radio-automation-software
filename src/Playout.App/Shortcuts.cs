using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Playout.App;

/// <summary>Keyboard shortcuts the operator can change: transport, the mic, streaming and the carts of the cartwall on screen.</summary>
static class Shortcuts
{
    public const string Play = "Play", Pause = "Pause", Next = "Next", Stop = "Stop", Mic = "Mic", Stream = "Stream";

    public static string Cart(int index) => $"Cart{index + 1}";

    /// <summary>Every action, in settings order: id and name.</summary>
    public static readonly (string Id, string Name)[] Actions =
    [
        (Play, L.T("Play")), (Pause, L.T("Pause / resume")), (Next, L.T("Next")), (Stop, L.T("Stop")), (Mic, L.T("Microphone")), (Stream, L.T("Stream on/off")),
        .. Enumerable.Range(0, AppSettings.CartCount).Select(i => (Cart(i), L.F("Cart {0}", i + 1))),
    ];

    public static Dictionary<string, string> Defaults() => new()
    {
        [Play] = "F5", [Pause] = "F6", [Next] = "F7", [Stop] = "F8", [Mic] = "F9",
    };

    public static string Name(string id) => Actions.First(a => a.Id == id).Name;

    public static KeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return KeyGesture.Parse(text); }
        catch { return null; }
    }

    static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>The key combination pressed, or null for a modifier on its own.</summary>
    public static KeyGesture? FromEvent(KeyEventArgs e) => IsModifier(e.Key) ? null : new KeyGesture(e.Key, e.KeyModifiers);

    static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    /// <summary>A plain key (no Ctrl, Alt or Cmd, not an F-key): it types text, so it must not fire while a text box has focus.</summary>
    public static bool TypesText(KeyGesture g) =>
        !IsFunctionKey(g.Key) && (g.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0;

    /// <summary>What a fixed key or combination already does, or null if it is free.</summary>
    public static string? Reserved(KeyGesture g, KeyModifiers command)
    {
        if (g.KeyModifiers == KeyModifiers.None)
            return g.Key switch
            {
                Key.Enter => L.T("playing the selected playlist item"),
                Key.Delete => L.T("removing playlist items"),
                Key.Tab => L.T("moving between controls"),
                Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown => L.T("moving through lists"),
                _ => null,
            };
        if (g.KeyModifiers != command) return null;
        return g.Key switch
        {
            Key.O => L.T("Add files"),
            Key.F => L.T("Search the library"),
            Key.S => L.T("Save playlist"),
            Key.E => L.T("Edit cue points"),
            Key.G => L.T("Edit the segue with the next item"),
            Key.T => L.T("Edit tags"),
            Key.Up or Key.Down => L.T("moving playlist items"),
            Key.OemComma => L.T("Settings"),
            _ => null,
        };
    }

    /// <summary>How a shortcut is shown, e.g. "Ctrl+1", "Num 5", "F5".</summary>
    public static string Display(KeyGesture? g)
    {
        if (g == null) return "";
        var parts = new List<string>();
        bool mac = OperatingSystem.IsMacOS();
        if (g.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (g.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add(mac ? "Option" : "Alt");
        if (g.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (g.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add(mac ? "Cmd" : "Win");
        parts.Add(g.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(g.Key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(g.Key - Key.NumPad0)}",
            Key.Add => "Num +",
            Key.Subtract => "Num -",
            Key.Multiply => "Num *",
            Key.Divide => "Num /",
            Key.Decimal => "Num .",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe or Key.OemBackslash => "\\",
            Key.OemTilde => "`",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Insert => "Ins",
            _ => g.Key.ToString(),
        });
        return string.Join("+", parts);
    }
}

/// <summary>
/// Shows a shortcut; click it (or press Space/Enter on it) and press a key or combination to change it.
/// Backspace or Delete clears it, Esc cancels.
/// </summary>
sealed class ShortcutBox : Button
{
    bool _capturing;
    KeyGesture? _gesture;
    Key? _swallowKeyUp; // the key just captured: releasing Space must not click the box into capturing again

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>Asked before a new shortcut (or null: cleared) is taken; return false to refuse it.</summary>
    public Func<KeyGesture?, bool>? Assigning;

    public ShortcutBox()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        Click += (_, _) => SetCapturing(true);
        LostFocus += (_, _) => SetCapturing(false);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key != _swallowKeyUp) return;
            _swallowKeyUp = null;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Show();
    }

    public KeyGesture? Gesture
    {
        get => _gesture;
        set { _gesture = value; Show(); }
    }

    void SetCapturing(bool on)
    {
        _capturing = on;
        Show();
    }

    void Show()
    {
        Content = _capturing ? L.T("Press a key…") : _gesture == null ? L.T("—") : Shortcuts.Display(_gesture);
        Classes.Set("capturing", _capturing);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (!_capturing || e.Key == Key.Tab) return; // Space/Enter start capturing (Click); Tab moves on
        e.Handled = true;
        _swallowKeyUp = e.Key;
        switch (e.Key)
        {
            case Key.Escape: SetCapturing(false); return;
            case Key.Back or Key.Delete when e.KeyModifiers == KeyModifiers.None:
                if (Assigning?.Invoke(null) != false) _gesture = null;
                SetCapturing(false);
                return;
        }
        if (Shortcuts.FromEvent(e) is not { } g) return; // a modifier on its own: wait for the key
        if (Assigning?.Invoke(g) != false) _gesture = g;
        SetCapturing(false);
    }
}
