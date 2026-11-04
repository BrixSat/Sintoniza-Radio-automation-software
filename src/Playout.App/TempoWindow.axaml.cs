using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Sintoniza Tempo: the studio clock, for a screen in the studio. The station's time (its time zone) on a clock face
/// with a ring of seconds, the date, the time to the top of the hour and, when the on-air program runs on this computer,
/// its red sign (MIC LIVE, ON AIR). Full screen with F11 or a double-click (Esc leaves it); the rest on the right-click menu.
/// </summary>
public partial class TempoWindow : Window
{
    readonly TempoSettings _settings = TempoSettings.Load();
    readonly DispatcherTimer _timer;

    public TempoWindow()
    {
        InitializeComponent();
        Localization.Apply(this);
        Localization.Apply(Menu); // not in the window's tree until it opens
        Title = AppInfo.TempoName;
        var app = AppSettings.Load();
        StationText.Text = app.StationName;
        AutostartItem.IsVisible = Autostart.Tempo.IsSupported;
        ShowTicks();
        FullScreenItem.Click += (_, _) => ToggleFullScreen();
        SignItem.Click += (_, _) => Option(s => s.ShowSign = !s.ShowSign);
        TopItem.Click += (_, _) => Option(s => s.ShowTopOfHour = !s.ShowTopOfHour);
        AutostartItem.Click += (_, _) =>
        {
            try { Autostart.Tempo.Set(!Autostart.Tempo.IsEnabled); }
            catch (Exception ex) { AppLog.Write($"{AppInfo.TempoName}: start with the computer not changed: {ex.Message}"); }
            ShowTicks();
        };
        CloseItem.Click += (_, _) => Close();
        DoubleTapped += (_, _) => ToggleFullScreen();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11) ToggleFullScreen();
            else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen) ToggleFullScreen();
        };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        // On the screen it was on (the studio's second screen), full screen there: never over the on-air program.
        if (_settings.Width >= MinWidth && _settings.Height >= MinHeight) (Width, Height) = (_settings.Width, _settings.Height);
        if (_settings.X is { } x && _settings.Y is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new Avalonia.PixelPoint(x, y);
        }
        Opened += (_, _) =>
        {
            // The screen it was on isn't there any more: in the middle of the main one.
            if (_settings.X is { } sx && _settings.Y is { } sy && Screens.ScreenFromPoint(new Avalonia.PixelPoint(sx + 20, sy + 20)) == null && Screens.Primary is { } main)
                Position = new Avalonia.PixelPoint(main.WorkingArea.X + 40, main.WorkingArea.Y + 40);
            if (_settings.FullScreen) WindowState = WindowState.FullScreen;
            Refresh();
            _timer.Start();
        };
        PositionChanged += (_, _) => Remember();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) Remember(fullScreenToo: true); // also left by the window manager (the green button…)
            else if (e.Property == BoundsProperty) Remember();
        };
        // In step with the face: the countdown changes with the seconds, the sign is looked at once a second.
        Clock.SecondChanged += Refresh;
        Closed += (_, _) => { _timer.Stop(); _settings.Save(); };
    }

    /// <summary>Where it is and how big (in a normal window only); and whether it is full screen.</summary>
    void Remember(bool fullScreenToo = false)
    {
        if (!IsVisible) return;
        if (fullScreenToo && WindowState != WindowState.Minimized) _settings.FullScreen = WindowState == WindowState.FullScreen;
        if (WindowState != WindowState.Normal) return;
        (_settings.X, _settings.Y) = (Position.X, Position.Y);
        (_settings.Width, _settings.Height) = (Width, Height);
    }

    void Option(Action<TempoSettings> set)
    {
        set(_settings);
        _settings.Save();
        ShowTicks();
        Refresh();
    }

    /// <summary>The menu's options, ticked as they are now (the tick drawn here: it is what the settings say).</summary>
    void ShowTicks()
    {
        static string Ticked(bool on, string text) => (on ? "✓  " : "     ") + L.T(text);
        SignItem.Header = Ticked(_settings.ShowSign, "Show the ON AIR sign");
        TopItem.Header = Ticked(_settings.ShowTopOfHour, "Show the time to the top of the hour");
        AutostartItem.Header = Ticked(Autostart.Tempo.IsEnabled, "Start with the computer");
    }

    void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        _settings.FullScreen = WindowState == WindowState.FullScreen;
        _settings.Save();
    }

    /// <summary>For the checks: the right-click menu's items.</summary>
    internal ContextMenu MenuForChecks => Menu;

    /// <summary>The date, the top of the hour and the sign (the clock face draws itself).</summary>
    void Refresh()
    {
        var now = Clock.Time;
        DateText.Text = now.ToString("dddd, d MMMM yyyy") + MainWindow.ZoneSuffix;
        var toTop = TimeSpan.FromHours(1) - new TimeSpan(0, now.Minute, now.Second);
        TopText.Text = L.F("Top of the hour in {0}", toTop.ToString(@"mm\:ss"));
        TopText.IsVisible = _settings.ShowTopOfHour;
        string? sign = _settings.ShowSign ? StudioSign.Read() : null;
        SignBox.IsVisible = sign != null;
        SignText.Text = sign ?? "";
    }
}
