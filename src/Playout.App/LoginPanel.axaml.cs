using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// The log-in: a user and password checked against the station's users. In the log-in window, and on the screen of a
/// program that is locked (the on-air program before anyone logs in, Sintoniza Pro after Log out).
/// </summary>
public partial class LoginPanel : UserControl
{
    UserStore? _users;

    /// <summary>The last user who logged in on this computer (filled in next time).</summary>
    public static string LastUser { get; set; } = "";

    /// <summary>Only users with it get in (null: any user).</summary>
    public string? Permission { get; set; }

    /// <summary>Who logged in.</summary>
    public event Action<SintonizaUser>? LoggedIn;

    /// <summary>Cancel was pressed (or Escape).</summary>
    public event Action? Cancelled;

    public LoginPanel()
    {
        InitializeComponent();
        LoginButton.Click += async (_, _) => await LogIn();
        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        // Enter logs in, Escape cancels (the panel may not be in a dialog of its own).
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await LogIn(); }
            else if (e.Key == Key.Escape && CancelButton.IsVisible) { e.Handled = true; Cancelled?.Invoke(); }
        }, RoutingStrategies.Tunnel);
    }

    /// <param name="reason">Why it asks (e.g. "Settings of this computer").</param>
    public void Setup(LibraryDb? db, string program, string? reason, string station, string? cancel)
    {
        _users = db == null ? null : new UserStore(db);
        ProgramText.Text = program;
        StationText.Text = station;
        Reason = reason;
        CancelButton.IsVisible = cancel != null;
        if (cancel != null) CancelButton.Content = cancel;
        Clear();
    }

    /// <summary>The station's users (null: not reachable yet; Log in waits).</summary>
    public LibraryDb? Db { set => _users = value == null ? null : new UserStore(value); }

    public string? Reason
    {
        set
        {
            ReasonText.Text = value ?? "";
            ReasonText.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public string Station { set => StationText.Text = value; }

    /// <summary>Ready for the next log-in: the last user filled in, no password, no error.</summary>
    public void Clear()
    {
        UserBox.Text = LastUser;
        PasswordBox.Text = "";
        ErrorText.IsVisible = false;
    }

    public void FocusFirst() => (string.IsNullOrEmpty(UserBox.Text) ? UserBox : PasswordBox).Focus();

    async Task LogIn()
    {
        if (!LoginButton.IsEnabled) return;
        if (_users is not { } users)
        {
            Show(L.T("The library isn't open yet: try again in a moment."));
            return;
        }
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
        if (user == null)
        {
            await Task.Delay(400); // slows guessing down
            LoginButton.IsEnabled = true;
            Show(L.T("Wrong user or password."));
            PasswordBox.Text = "";
            PasswordBox.Focus();
            return;
        }
        LoginButton.IsEnabled = true;
        if (Permission != null && !user.Can(Permission))
        {
            Show(L.F("{0} may not do this here.", user.Name));
            return;
        }
        LastUser = user.Name;
        PasswordBox.Text = "";
        ErrorText.IsVisible = false;
        LoggedIn?.Invoke(user);
    }

    void Show(string error)
    {
        ErrorText.Text = error;
        ErrorText.IsVisible = true;
    }
}
