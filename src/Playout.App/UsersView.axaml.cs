using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Playout.Library;

namespace Playout.App;

/// <summary>Sintoniza Pro → Users: the station's users and what each may use.</summary>
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
    ];

    readonly UserStore? _store;
    readonly string _me = "";
    readonly Dictionary<string, CheckBox> _boxes = new();
    readonly Dictionary<long, CheckBox> _stationBoxes = new();
    bool _stationsRead;
    List<SintonizaUser> _users = new();

    // Parameterless constructor for the XAML previewer.
    public UsersView() : this(null, "") { }

    /// <param name="me">Who is logged in (can't delete themselves or take away their own Users permission).</param>
    public UsersView(UserStore? store, string me)
    {
        InitializeComponent();
        Localization.Apply(this);
        _store = store;
        _me = me;
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
            ShowUser(new SintonizaUser { Name = "", Enabled = true, Permissions = Permission.Presenter.ToHashSet() });
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

    async Task Load(string? select)
    {
        if (_store is not { } store) return;
        try { _users = await Task.Run(store.All); }
        catch (Exception ex) { MessageText.Text = ex.Message; return; }
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
        int i = _users.FindIndex(u => u.Name == (select ?? _editing?.Name ?? _me));
        UserList.SelectedIndex = Math.Max(0, i);
    }

    void ShowUser(SintonizaUser? user = null)
    {
        user ??= UserList.SelectedIndex is >= 0 and var i && i < _users.Count ? _users[i] : null;
        _editing = user;
        EditPanel.IsEnabled = user != null;
        DeleteButton.IsEnabled = user is { Id: > 0 } && user.Name != _me;
        if (user == null) return;
        NameBox.Text = user.Name;
        PasswordBox.Text = "";
        PasswordBox.PlaceholderText = user.Id == 0 ? L.T("(needed)") : L.T("(unchanged)");
        EnabledBox.IsChecked = user.Enabled;
        EnabledBox.IsEnabled = user.Name != _me;
        foreach (var (key, box) in _boxes) box.IsChecked = user.Can(key);
        _boxes[Permission.Users].IsEnabled = user.Name != _me; // nobody locks themselves out
        AllStationsBox.IsChecked = user.Stations.Count == 0;
        foreach (var (id, box) in _stationBoxes) box.IsChecked = user.Stations.Count == 0 || user.Stations.Contains(id);
        // Nobody shuts themselves out of a station; and whoever may work only on some gives only those.
        var mine = _users.FirstOrDefault(u => u.Name == _me)?.Stations ?? new();
        AllStationsBox.IsEnabled = user.Name != _me && mine.Count == 0;
        StationBoxes.IsEnabled = user.Stations.Count > 0 && user.Name != _me;
        foreach (var (id, box) in _stationBoxes) box.IsEnabled = mine.Count == 0 || mine.Contains(id);
    }

    async Task SaveUser()
    {
        if (_store is not { } store || _editing is not { } user) return;
        string name = NameBox.Text?.Trim() ?? "";
        if (name.Length == 0) { MessageText.Text = L.T("Give the user a name."); return; }
        if (_users.Any(u => u.Id != user.Id && u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageText.Text = L.T("There is already a user with this name."); return; }
        string? password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
        if (user.Id == 0 && password == null) { MessageText.Text = L.T("A new user needs a password."); return; }
        var changed = new SintonizaUser
        {
            Id = user.Id, Name = name, Enabled = EnabledBox.IsChecked == true,
            Permissions = _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet(),
            // The stations list couldn't be read: as they were. Otherwise all, or those ticked.
            Stations = !_stationsRead ? user.Stations.ToHashSet()
                : !StationsPanel.IsVisible || AllStationsBox.IsChecked == true ? new()
                : _stationBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet(),
        };
        var mine = _users.FirstOrDefault(u => u.Name == _me)?.Stations ?? new();
        // Only when the stations change: a password or a name can always be saved.
        if (mine.Count > 0 && user.Name != _me && !changed.Stations.SetEquals(user.Stations)
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
        if (_store is not { } store || _editing is not { Id: > 0 } user || user.Name == _me) return;
        if (TopLevel.GetTopLevel(this) is not Window owner || !await Dialogs.Confirm(owner, L.T("Delete user"), L.F("Delete \"{0}\"?", user.Name), L.T("Delete"))) return;
        await Task.Run(() => store.Delete(user.Id));
        _editing = null;
        await Load(_me);
    }
}
