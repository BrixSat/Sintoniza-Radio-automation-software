using Avalonia.Controls;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Asks for a Sintoniza user (Sintoniza Pro and Sintoniza Import when they open, the playout's Settings, …). With a
/// permission, only a user who has it gets in. The user it closes with (or null) is the answer.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>The last user who logged in here (filled in next time).</summary>
    public static string LastUser
    {
        get => LoginPanel.LastUser;
        set => LoginPanel.LastUser = value;
    }

    // Parameterless constructor for the XAML previewer.
    public LoginWindow() : this(null, "", null, null) { }

    /// <param name="permission">Only users with it (null: any user).</param>
    /// <param name="reason">Why it asks (e.g. "Settings of this computer").</param>
    public LoginWindow(LibraryDb? db, string program, string? permission, string? reason, string station = "")
    {
        InitializeComponent();
        Localization.Apply(this);
        Title = L.F("{0} — log in", program);
        Panel.Permission = permission;
        Panel.Setup(db, program, reason, station, L.T("Cancel"));
        Panel.LoggedIn += user =>
        {
            if (LoggedIn != null) LoggedIn(user);
            else Close(user);
        };
        Panel.Cancelled += () => Close(null);
        Opened += (_, _) => Panel.FocusFirst();
    }

    /// <summary>When it isn't a dialog (a program's first window): who logged in.</summary>
    public event Action<SintonizaUser>? LoggedIn;
}
