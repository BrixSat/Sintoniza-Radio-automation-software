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
        AttachedToVisualTree += async (_, _) => await Load(null);
    }

    SintonizaUser? _editing;

    async Task Load(string? select)
    {
        if (_store is not { } store) return;
        try { _users = await Task.Run(store.All); }
        catch (Exception ex) { MessageText.Text = ex.Message; return; }
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
        };
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
