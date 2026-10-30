using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>A listener's request, taken by the staff (in Sintoniza Emi or the web remote), waiting for the presenter.</summary>
public sealed record SongRequest(Guid Id, string Path, string Title, string Artist, string Listener, string Message, string TakenBy, DateTime At)
{
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";

    /// <summary>Who it is for and what they said, for the presenter (the item's note on air).</summary>
    public string Dedication => (Listener.Trim(), Message.Trim()) switch
    {
        ({ Length: > 0 } who, { Length: > 0 } what) => L.F("Request from {0}: {1}", who, what),
        ({ Length: > 0 } who, _) => L.F("Request from {0}", who),
        (_, { Length: > 0 } what) => L.F("Request: {0}", what),
        _ => L.T("A listener's request"),
    };
}

/// <summary>
/// Listener requests: the staff take them (phone, messages) and put them in a list; the presenter puts each on air
/// next, or turns it down. Nothing is open to the internet; the list is this studio's (gone at a restart).
/// </summary>
public partial class MainWindow
{
    readonly List<SongRequest> _requests = new();

    /// <summary>The requests waiting changed (on the UI thread).</summary>
    public event Action? RequestsChanged;

    public IReadOnlyList<SongRequest> Requests => _requests;

    /// <summary>Songs in the library for a request (off the UI thread): path, title, artist, length.</summary>
    public List<(string Path, string Title, string Artist, double Seconds)> SearchSongs(string text)
    {
        if (_library.Db is not { } db || text.Trim().Length < 2) return new();
        return db.Search(text, CategoryFilter.All, 30).Tracks.Where(t => !t.Missing)
            .Select(t => (t.Path, t.Title.Length > 0 ? t.Title : Path.GetFileNameWithoutExtension(t.Path), t.Artist, t.Duration)).ToList();
    }

    /// <summary>Takes a request: in the list for the presenter. The reason when it can't be (the file isn't there).</summary>
    public string? AddRequest(string path, string title, string artist, string listener, string message, string takenBy)
    {
        if (!SharedPaths.FileExists(path)) return L.F("Not found: {0}", path);
        var request = new SongRequest(Guid.NewGuid(), path, title.Trim(), artist.Trim(), listener.Trim(), message.Trim(), takenBy, DateTime.Now);
        _requests.Add(request);
        ShowMessage(L.F("Request taken: {0} ({1})", request.Display, request.Dedication));
        RequestsChanged?.Invoke();
        ShowRequestCount();
        return null;
    }

    /// <summary>The presenter puts a request on next, with who it is for as its note (shown on air).</summary>
    public void AcceptRequest(Guid id)
    {
        if (_engine is not { } engine || _requests.FirstOrDefault(r => r.Id == id) is not { } request) return;
        _requests.Remove(request);
        string label = request.Listener.Length > 0 ? L.F("Request: {0}", request.Listener) : L.T("Request");
        engine.AddScheduled(label, [new ScheduledItem(request.Path, Note: request.Dedication)], NextInsertIndex());
        ShowMessage(L.F("Request plays next: {0}", request.Display));
        RequestsChanged?.Invoke();
        ShowRequestCount();
    }

    public void DeclineRequest(Guid id)
    {
        if (_requests.FirstOrDefault(r => r.Id == id) is not { } request) return;
        _requests.Remove(request);
        ShowMessage(L.F("Request turned down: {0}", request.Display));
        RequestsChanged?.Invoke();
        ShowRequestCount();
    }

    /// <summary>The Requests button says how many are waiting.</summary>
    void ShowRequestCount()
    {
        RequestsButton.Content = _requests.Count > 0 ? L.F("Requests ({0})…", _requests.Count) : L.T("Requests…");
        RequestsButton.Classes.Set("accent", _requests.Count > 0);
    }

    RequestsWindow? _requestsWindow;

    void OpenRequests()
    {
        if (_requestsWindow is { } open) { open.Activate(); return; }
        _requestsWindow = new RequestsWindow(this);
        _requestsWindow.Closed += (_, _) => _requestsWindow = null;
        _requestsWindow.Show(this);
    }

    public string CurrentUserName => _user?.Name ?? AppInfo.UserName;
}
