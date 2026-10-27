using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Listening in Sintoniza Pro: a file played on the Preview output (Settings → Audio), never on the on-air cards, with
/// play / pause, stop and a position to drag. The page says what is selected (<see cref="Selected"/>).
/// </summary>
public partial class ListenBar : UserControl
{
    PreviewDeck? _deck;
    PreviewPlayer? _player;
    readonly DispatcherTimer _timer;

    public ListenBar()
    {
        InitializeComponent();
        Localization.Apply(this);
        ListenButton.Click += (_, _) => ListenOrPause();
        ListenStopButton.Click += (_, _) => Stop();
        ListenSlider.AddHandler(PointerReleasedEvent, (_, _) => _player?.Seek(ListenSlider.Value), RoutingStrategies.Tunnel, true);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Show());
    }

    /// <summary>This computer's settings: where the Preview output is.</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>The selected item's file and title (null: none), for ▶ and Space.</summary>
    public Func<(string Path, string Title)?>? Selected { get; set; }

    /// <summary>Why it can't listen (for the page's status bar).</summary>
    public event Action<string>? Problem;

    /// <summary>The file playing (or paused), or null.</summary>
    public string? Path { get; private set; }

    /// <summary>The Preview output, opened the first time it is needed (also for the segue editor and voice tracks).</summary>
    public PreviewDeck Deck() => _deck ??= new PreviewDeck((Settings ?? new AppSettings()).Routing, MainWindow.ForcedDeviceFromArgs());

    /// <summary>Space and ▶: the selected item, or pause / carry on with the one playing.</summary>
    public void ListenOrPause()
    {
        var selected = Selected?.Invoke();
        if (_player != null && (selected == null || selected.Value.Path == Path))
        {
            if (_player.IsPlaying) _player.Pause(); else _player.Play();
            Show();
            return;
        }
        if (selected is { } s) Listen(s.Path, s.Title);
    }

    /// <summary>Space anywhere on the page but in a text box: <see cref="ListenOrPause"/>.</summary>
    public void SpaceListens(Control page) => page.KeyDown += (_, e) =>
    {
        if (e.Key != Key.Space || e.Source is TextBox) return;
        e.Handled = true;
        ListenOrPause();
    };

    public void Listen(string path, string title)
    {
        Stop();
        try
        {
            var deck = Deck();
            ListenWarning.Text = L.T("The Preview output is the on-air one (Settings → Audio): this would be heard on air.");
            ListenWarning.IsVisible = deck.PreviewIsOnAir;
            _player = deck.Open(path);
        }
        catch (Exception ex)
        {
            Problem?.Invoke(L.F("Can't listen: {0}", ex.Message));
            return;
        }
        if (_player == null)
        {
            Problem?.Invoke(L.F("Can't listen: {0} can't be opened", System.IO.Path.GetFileName(path)));
            return;
        }
        Path = path;
        ListenTitle.Text = title;
        ListenSlider.Maximum = Math.Max(1, _player.Length);
        _player.Play();
        _timer.Start();
        Show();
    }

    public void Stop()
    {
        _player?.Dispose();
        _player = null;
        Path = null;
        _timer.Stop();
        ListenTitle.Text = "";
        ListenSlider.Value = 0;
        Show();
    }

    /// <summary>The page closes: listening stops, the sound card is let go.</summary>
    public void Close()
    {
        Stop();
        _deck?.Dispose();
        _deck = null;
    }

    void Show()
    {
        ListenButton.Content = _player?.IsPlaying == true ? "❚❚" : "▶";
        double at = _player?.Position ?? 0, length = _player?.Length ?? 0;
        ListenTime.Text = $"{Format.Duration(at)} / {Format.Duration(length)}";
        if (_player != null && !ListenSlider.IsPointerOver) ListenSlider.Value = at;
    }
}
