using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Playout.Engine;

namespace Playout.App;

/// <summary>A request as listed for the presenter.</summary>
public sealed class RequestRow(SongRequest request)
{
    public SongRequest Request { get; } = request;
    public string Song => Request.Display;
    public string Dedication => Request.Dedication;
    public string Taken => L.F("{0:HH:mm} · taken by {1}", Request.At, Request.TakenBy);
}

/// <summary>A next item as listed in the presenter view: the first one larger.</summary>
public sealed record PresenterNextRow(string Text, string Length, double Size, FontWeight Weight);

/// <summary>
/// A large, simple screen for a live show (opened when a show starts, or by Presenter view): what is on air and its
/// talk-up, what comes next, the countdowns, the show's notes and the listeners' requests, with Play, Next and Stop.
/// </summary>
public partial class PresenterWindow : Window
{
    readonly MainWindow _main;
    readonly DispatcherTimer _timer;
    static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D")), Amber = new SolidColorBrush(Color.Parse("#F5A524"));

    public PresenterWindow() : this(null!) { }

    public PresenterWindow(MainWindow main)
    {
        InitializeComponent();
        Localization.Apply(this);
        _main = main;
        PlayButton.Click += (_, _) => _main.PresenterPlay();
        NextButton.Click += async (_, _) => await _main.PresenterNext();
        StopButton.Click += async (_, _) => await _main.PresenterStop();
        RequestList.SelectionChanged += (_, _) => AcceptButton.IsEnabled = DeclineButton.IsEnabled = RequestList.SelectedItem != null;
        AcceptButton.Click += (_, _) => { if (RequestList.SelectedItem is RequestRow r) _main.AcceptRequest(r.Request.Id); };
        DeclineButton.Click += (_, _) => { if (RequestList.SelectedItem is RequestRow r) _main.DeclineRequest(r.Request.Id); };
        _main.RequestsChanged += ShowRequests;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Refresh());
        Opened += (_, _) => { ShowRequests(); Refresh(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _main.RequestsChanged -= ShowRequests; };
    }

    void ShowRequests()
    {
        RequestList.ItemsSource = _main.Requests.Select(r => new RequestRow(r)).ToList();
        RequestsCaption.Text = _main.Requests.Count > 0 ? L.F("REQUESTS ({0})", _main.Requests.Count) : L.T("REQUESTS");
    }

    static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    void Refresh()
    {
        var s = _main.PresenterNow();
        var now = DateTime.Now;
        ShowText.Text = s.ShowName.Length > 0 ? s.ShowName : L.T("No show on air");
        PresenterText.Text = s.Presenter;
        ShowEndsText.Text = s.ShowEnds is { } ends && ends > now ? Clock(ends - now) : "—";
        ShowEndsText.Foreground = s.ShowEnds is { } e && e - now <= TimeSpan.FromMinutes(1) ? Red : Brushes.White;
        ShowNotesText.Text = s.ShowNotes;
        ShowNotesBox.IsVisible = s.ShowNotes.Length > 0;

        TitleText.Text = s.OnAir?.Title ?? L.T("Nothing on air");
        ArtistText.Text = s.OnAir == null ? "" : s.OnAir.IsPause ? L.T("Pause: no sound, the presenter talks") : s.OnAir.Artist;
        RemainingText.Text = s.OnAir == null ? "—" : s.OpenEnded ? "∞" : "-" + Format.Duration(Math.Ceiling(s.Remaining));
        RemainingText.Foreground = s.OnAir != null && !s.OpenEnded && s.Remaining <= 10 ? Red : Brushes.White;
        TalkUpPanel.IsVisible = s.TalkUp is > 0;
        TalkUpText.Text = s.TalkUp is { } talk ? Format.Duration(Math.Ceiling(talk)) : "";
        NoteNowText.Text = s.NoteNow ?? "";
        NoteNowText.IsVisible = !string.IsNullOrWhiteSpace(s.NoteNow);

        NextList.ItemsSource = s.Next.Select((t, i) => new PresenterNextRow(t.Display, t.IsOpenEnded ? "∞" : Format.Duration(t.PlayLength),
            i == 0 ? 24 : 17, i == 0 ? FontWeight.Bold : FontWeight.Normal)).ToList();
        NoteNextText.Text = s.NoteNext is { Length: > 0 } n ? L.F("📝 NEXT · {0}", n) : "";
        NoteNextText.IsVisible = NoteNextText.Text.Length > 0;

        var toTop = TimeSpan.FromHours(1) - new TimeSpan(0, now.Minute, now.Second);
        TopText.Text = "-" + toTop.ToString(@"mm\:ss");
        TopText.Foreground = toTop <= TimeSpan.FromMinutes(1) ? Amber : Brushes.White;
        BreakText.Text = s.Break is { } b && b.At > now ? "-" + Clock(b.At - now) : "—";
        BreakText.Foreground = s.Break is { } br && br.At - now <= TimeSpan.FromMinutes(1) ? Amber : Brushes.White;

        ModeText.Text = s.Auto ? L.T("AUTO") : L.T("ASSISTED: each item waits for PLAY");
        ModeText.Foreground = s.Auto ? Brushes.White : Amber;
        PlayButton.IsEnabled = s.State != PlayerState.Playing || s.OnAir == null;
    }
}
