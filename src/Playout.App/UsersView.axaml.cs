using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Sintoniza Pro → Users: the users and what each may use. Whoever works on every station manages everyone; whoever
/// works only on some (a station's own admin) sees and manages only the users of those stations, and gives only them.
/// </summary>
public partial class UsersView : UserControl
{
    static readonly (string Key, string Name, string Help)[] Permissions =
    [
        (Permission.OnAir, "On air", "The on-air program in the studio: playing, the playlist, the cartwall, the microphone"),
        (Permission.Library, "Library", "Songs, categories, importing music"),
        (Permission.Programming, "Programming", "Hour templates, daily playlists, events"),
        (Permission.Voice, "Voice tracks", "Recording voice tracks (and looking at the daily playlists)"),
        (Permission.Traffic, "Traffic", "Ad breaks, clients and campaigns"),
        (Permission.Station, "Station", "The station's settings and cartwalls"),
        (Permission.Users, "Users", "Adding and changing users"),
        (Permission.Computer, "This computer's settings", "A playout's own Settings (sound cards, database, keys…)"),
        (Permission.Remote, "Web remote", "Controlling a playout from a phone"),
        (Permission.Stream, "Stream", "Starting and stopping the internet stream by hand (STREAM)"),
    ];

    readonly UserStore? _store;
    /// <summary>Who is logged in, by number (a name can be changed); null: no log-in (everything open).</summary>
    readonly long? _meId;
    /// <summary>Their name as it is now.</summary>
    string _me = "";
    readonly Dictionary<string, CheckBox> _boxes = new();
    readonly Dictionary<long, CheckBox> _stationBoxes = new();
    bool _stationsRead;
    List<SintonizaUser> _users = new();
    /// <summary>Every user (for names already taken, also those not shown here).</summary>
    List<SintonizaUser> _all = new();
    /// <summary>The stations of who is logged in (empty: all of them).</summary>
    HashSet<long> _mine = new();

    // Parameterless constructor for the XAML previewer.
    public UsersView() : this(null, null) { }

    /// <param name="me">Who is logged in (can't delete themselves or take away their own Users permission).</param>
    public UsersView(UserStore? store, SintonizaUser? me)
    {
        InitializeComponent();
        Localization.Apply(this);
        _store = store;
        _meId = me?.Id;
        _me = me?.Name ?? "";
        foreach (var (key, name, help) in Permissions)
        {
            var box = new CheckBox { Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = L.T(name), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = L.T(help), FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#9AA3B2")) },
                },
            } };
            _boxes[key] = box;
            PermissionPanel.Children.Add(box);
        }
        UserList.SelectionChanged += (_, _) => ShowUser();
        AddButton.Click += (_, _) =>
        {
            UserList.SelectedItem = null;
            // A station's admin makes users of their own stations.
            ShowUser(new SintonizaUser { Name = "", Enabled = true, Permissions = Permission.Presenter.ToHashSet(), Stations = _mine.ToHashSet() });
            NameBox.Focus();
        };
        DeleteButton.Click += async (_, _) => await Delete();
        SaveUserButton.Click += async (_, _) => await SaveUser();
        AdminPresetButton.Click += (_, _) => { foreach (var (k, b) in _boxes) b.IsChecked = true; };
        PresenterPresetButton.Click += (_, _) => { foreach (var (k, b) in _boxes) b.IsChecked = Permission.Presenter.Contains(k); };
        AllStationsBox.IsCheckedChanged += (_, _) => StationBoxes.IsEnabled = AllStationsBox.IsChecked != true;
        AttachedToVisualTree += async (_, _) => await Load(null);
    }

    SintonizaUser? _editing;

    bool IsMe(SintonizaUser u) => _meId != null && u.Id == _meId;

    async Task Load(string? select)
    {
        if (_store is not { } store) return;
        try { _all = await Task.Run(store.All); }
        catch (Exception ex) { MessageText.Text = ex.Message; return; }
        var self = _all.FirstOrDefault(u => u.Id == _meId);
        if (_meId != null && self == null)
        {
            // Who is logged in isn't a user any more: nothing to manage (never everything).
            _users = new();
            UserList.ItemsSource = null;
            EditPanel.IsEnabled = AddButton.IsEnabled = DeleteButton.IsEnabled = false;
            MessageText.Text = L.T("You aren't a user any more: log in again.");
            return;
        }
        _me = self?.Name ?? "";
        _mine = self?.Stations ?? new();
        // Only some stations: the users of those (not one who also works elsewhere, nor one on every station).
        _users = _mine.Count == 0 ? _all : _all.Where(u => IsMe(u) || u.Stations.Count > 0 && u.Stations.IsSubsetOf(_mine)).ToList();
        // Several stations in the library: which each user may work on.
        if (_stationBoxes.Count == 0)
        {
            List<StationInfo> stations;
            try { stations = await Task.Run(store.Db.Stations); _stationsRead = true; }
            catch { stations = new(); }
            foreach (var station in stations)
            {
                var box = new CheckBox { Content = station.Name };
                _stationBoxes[station.Id] = box;
                StationBoxes.Children.Add(box);
            }
            StationsPanel.IsVisible = stations.Count > 1;
        }
        UserList.ItemsSource = _users.Select(u => u.Enabled ? u.Name : $"{u.Name}  ({L.T("can't log in")})").ToList();
        int i = _users.FindIndex(u => select != null ? u.Name == select : _editing != null ? u.Id == _editing.Id : IsMe(u));
        UserList.SelectedIndex = Math.Max(0, i);
    }

    void ShowUser(SintonizaUser? user = null)
    {
        user ??= UserList.SelectedIndex is >= 0 and var i && i < _users.Count ? _users[i] : null;
        _editing = user;
        EditPanel.IsEnabled = user != null;
        DeleteButton.IsEnabled = user is { Id: > 0 } && !IsMe(user);
        if (user == null) return;
        NameBox.Text = user.Name;
        PasswordBox.Text = "";
        PasswordBox.PlaceholderText = user.Id == 0 ? L.T("(needed)") : L.T("(unchanged)");
        EnabledBox.IsChecked = user.Enabled;
        EnabledBox.IsEnabled = !IsMe(user);
        foreach (var (key, box) in _boxes) box.IsChecked = user.Can(key);
        _boxes[Permission.Users].IsEnabled = !IsMe(user); // nobody locks themselves out
        AllStationsBox.IsChecked = user.Stations.Count == 0;
        foreach (var (id, box) in _stationBoxes) box.IsChecked = user.Stations.Count == 0 || user.Stations.Contains(id);
        // Nobody shuts themselves out of a station; and whoever may work only on some gives only those.
        var mine = _mine;
        AllStationsBox.IsEnabled = !IsMe(user) && mine.Count == 0;
        StationBoxes.IsEnabled = user.Stations.Count > 0 && !IsMe(user);
        foreach (var (id, box) in _stationBoxes) box.IsEnabled = mine.Count == 0 || mine.Contains(id);
    }

    async Task SaveUser()
    {
        if (_store is not { } store || _editing is not { } user) return;
        string name = NameBox.Text?.Trim() ?? "";
        if (name.Length == 0) { MessageText.Text = L.T("Give the user a name."); return; }
        if (_all.Any(u => u.Id != user.Id && u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageText.Text = L.T("There is already a user with this name."); return; }
        string? password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
        if (user.Id == 0 && password == null) { MessageText.Text = L.T("A new user needs a password."); return; }
        var changed = new SintonizaUser
        {
            Id = user.Id, Name = name, Enabled = EnabledBox.IsChecked == true,
            Permissions = _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet(),
            // The stations list couldn't be read: as they were. Otherwise all, or those ticked.
            // One station only (nothing to choose): as they were.
            Stations = !_stationsRead || !StationsPanel.IsVisible ? user.Stations.ToHashSet()
                : AllStationsBox.IsChecked == true ? new()
                : _stationBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet(),
        };
        var mine = _mine;
        // Only when the stations change: a password or a name can always be saved.
        // Nobody changes their own stations (an admin of some can't make themselves one of all).
        if (IsMe(user) && !changed.Stations.SetEquals(user.Stations))
        {
            MessageText.Text = L.T("Your own stations are changed by another admin.");
            return;
        }
        if (mine.Count > 0 && !IsMe(user) && !changed.Stations.SetEquals(user.Stations)
            && (changed.Stations.Count == 0 || !changed.Stations.Except(user.Stations).All(mine.Contains)))
        {
            MessageText.Text = L.T("You may only give the stations you work on yourself.");
            return;
        }
        if (StationsPanel.IsVisible && AllStationsBox.IsChecked != true && changed.Stations.Count == 0)
        {
            MessageText.Text = L.T("Tick at least one station, or All stations.");
            return;
        }
        try
        {
            await Task.Run(() => store.Save(changed, password));
            MessageText.Text = L.F("Saved {0}.", name);
            _editing = changed;
            await Load(name);
        }
        catch (Exception ex) { MessageText.Text = L.F("Not saved: {0}", ex.Message); }
    }

    async Task Delete()
    {
        if (_store is not { } store || _editing is not { Id: > 0 } user || IsMe(user)) return;
        if (TopLevel.GetTopLevel(this) is not Window owner || !await Dialogs.Confirm(owner, L.T("Delete user"), L.F("Delete \"{0}\"?", user.Name), L.T("Delete"))) return;
        await Task.Run(() => store.Delete(user.Id));
        _editing = null;
        await Load(null);
    }
}
