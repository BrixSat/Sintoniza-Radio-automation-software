using Avalonia.Controls;
using Avalonia.Threading;

namespace Playout.App;

/// <summary>A song found for a request.</summary>
public sealed record RequestSong(string Path, string Title, string Artist, string Length);

/// <summary>Listeners' requests: a song from the library with who it is for and their message; those waiting, to put on next or turn down.</summary>
public partial class RequestsWindow : Window
{
    readonly MainWindow _main;
    readonly DispatcherTimer _searchDelay;
    int _search;

    public RequestsWindow() : this(null!) { }

    public RequestsWindow(MainWindow main)
    {
        InitializeComponent();
        Localization.Apply(this);
        _main = main;
        _searchDelay = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, async (_, _) => await Search());
        SearchBox.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };
        SongList.SelectionChanged += (_, _) => TakeButton.IsEnabled = SongList.SelectedItem != null;
        TakeButton.Click += (_, _) => Take();
        WaitingList.SelectionChanged += (_, _) => AcceptButton.IsEnabled = DeclineButton.IsEnabled = WaitingList.SelectedItem != null;
        AcceptButton.Click += (_, _) => { if (WaitingList.SelectedItem is RequestRow r) _main.AcceptRequest(r.Request.Id); };
        DeclineButton.Click += (_, _) => { if (WaitingList.SelectedItem is RequestRow r) _main.DeclineRequest(r.Request.Id); };
        _main.RequestsChanged += ShowWaiting;
        Opened += (_, _) => { ShowWaiting(); SearchBox.Focus(); };
        Closed += (_, _) => _main.RequestsChanged -= ShowWaiting;
    }

    async Task Search()
    {
        _searchDelay.Stop();
        string text = SearchBox.Text ?? "";
        int version = ++_search;
        var songs = await Task.Run(() => _main.SearchSongs(text));
        if (version != _search) return;
        SongList.ItemsSource = songs.Select(s => new RequestSong(s.Path, s.Title, s.Artist, Format.Duration(s.Seconds))).ToList();
    }

    void Take()
    {
        if (SongList.SelectedItem is not RequestSong song) return;
        if (_main.AddRequest(song.Path, song.Title, song.Artist, ListenerBox.Text ?? "", MessageBox.Text ?? "", _main.CurrentUserName) is { } problem)
        {
            StatusText.Text = problem;
            return;
        }
        StatusText.Text = L.F("Taken: {0}", string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Artist} - {song.Title}");
        ListenerBox.Text = MessageBox.Text = "";
        SongList.SelectedItem = null;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    void ShowWaiting()
    {
        WaitingList.ItemsSource = _main.Requests.Select(r => new RequestRow(r)).ToList();
        WaitingText.Text = _main.Requests.Count == 0 ? L.T("No requests waiting") : L.F("Waiting ({0})", _main.Requests.Count);
    }
}
