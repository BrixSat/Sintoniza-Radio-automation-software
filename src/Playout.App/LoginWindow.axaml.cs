using Avalonia.Controls;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Asks for a Sintoniza user (Sintoniza Pro and Sintoniza Import when they open, the playout's Settings, …). With a
/// permission, only a user who has it gets in. The user it closes with (or null) is the answer.
/// </summary>
public partial class LoginWindow : Window
{
    readonly UserStore? _users;
    readonly string? _permission;

    /// <summary>The last user who logged in here (filled in next time).</summary>
    public static string LastUser { get; set; } = "";

    // Parameterless constructor for the XAML previewer.
    public LoginWindow() : this(null, "", null, null) { }

    /// <param name="permission">Only users with it (null: any user).</param>
    /// <param name="reason">Why it asks (e.g. "Settings of this computer").</param>
    public LoginWindow(LibraryDb? db, string program, string? permission, string? reason, string station = "")
    {
        InitializeComponent();
        Localization.Apply(this);
        _users = db == null ? null : new UserStore(db);
        _permission = permission;
        Title = L.F("{0} — log in", program);
        ProgramText.Text = program;
        StationText.Text = station;
        ReasonText.Text = reason ?? "";
        ReasonText.IsVisible = !string.IsNullOrEmpty(reason);
        UserBox.Text = LastUser;
        LoginButton.Click += async (_, _) => await LogIn();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => (string.IsNullOrEmpty(UserBox.Text) ? UserBox : PasswordBox).Focus();
    }

    /// <summary>When it isn't a dialog (a program's first window): who logged in.</summary>
    public event Action<SintonizaUser>? LoggedIn;

    async Task LogIn()
    {
        if (_users is not { } users) return;
        string name = UserBox.Text?.Trim() ?? "", password = PasswordBox.Text ?? "";
        LoginButton.IsEnabled = false;
        SintonizaUser? user;
        try { user = await Task.Run(() => users.LogIn(name, password)); }
        catch (Exception ex)
        {
            Show(L.F("The users can't be read: {0}", ex.Message));
            LoginButton.IsEnabled = true;
            return;
        }
        LoginButton.IsEnabled = true;
        if (user == null)
        {
            await Task.Delay(400); // slows guessing down
            Show(L.T("Wrong user or password."));
            PasswordBox.Text = "";
            PasswordBox.Focus();
            return;
        }
        if (_permission != null && !user.Can(_permission))
        {
            Show(L.F("{0} may not do this here.", user.Name));
            return;
        }
        LastUser = user.Name;
        if (LoggedIn != null) LoggedIn(user);
        else Close(user);
    }

    void Show(string error)
    {
        ErrorText.Text = error;
        ErrorText.IsVisible = true;
    }
}
