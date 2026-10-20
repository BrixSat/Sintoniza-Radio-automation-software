using Avalonia.Controls;
using Playout.Library;

namespace Playout.App;

/// <summary>
/// Who is working on air. The program opens locked: what is on air carries on (AUTO plays, the schedule runs, the web
/// remote works), but nothing on screen or on the keyboard can be used until a user with the On air permission logs
/// in. Log out locks it again. Without the library there are no users to ask for: it stays open, so the station can
/// still be run.
/// </summary>
public partial class MainWindow
{
    SintonizaUser? _user;
    bool _locked;

    void WireLogin()
    {
        LoginPanel.LastUser = _settings.LastUser;
        LockLogin.Permission = Permission.OnAir;
        LockLogin.Setup(null, AppInfo.Name, L.T("Log in to work on air."), _settings.StationName, null);
        LockLogin.LoggedIn += user =>
        {
            _settings.LastUser = user.Name;
            AppLog.Write($"On air: {user.Name} logged in");
            LogIn(user);
        };
        LogoutButton.Click += (_, _) =>
        {
            if (_user != null) AppLog.Write($"On air: {_user.Name} logged out");
            Lock();
        };
        _library.Changed += LibraryForLogin;
        // Closing the program would take the station off air: only someone logged in may (the computer shutting
        // down, an update or the program itself still close it).
        Closing += (_, e) =>
        {
            if (!_locked || e.IsProgrammatic || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            LockNote.Text = L.T("Log in to close the program.");
        };
        Opened += (_, _) => { if (_locked) LockLogin.FocusFirst(); };
        Lock();
    }

    /// <summary>Nobody logged in: the screen and the keys do nothing until someone does.</summary>
    void Lock()
    {
        _user = null;
        _locked = true;
        _settingsUnlockedUntil = default;
        _keysDown.Clear(); // a held microphone key closes
        _micKeyHeld = false;
        UpdateMic();
        Studio.IsEnabled = false;
        LockOverlay.IsVisible = true;
        LogoutButton.IsVisible = false;
        LockNote.Text = L.T("The station stays on air while nobody is logged in.");
        LockLogin.Station = _settings.StationName;
        LockLogin.Clear();
        LibraryForLogin();
        LockLogin.FocusFirst();
    }

    /// <summary>Logged in (null: without the library, nobody can be asked for).</summary>
    internal void LogIn(SintonizaUser? user)
    {
        _user = user;
        _locked = false;
        LockOverlay.IsVisible = false;
        Studio.IsEnabled = true;
        LogoutButton.IsVisible = true;
        LogoutText.Text = user != null ? L.F("Log out ({0})", user.Name) : L.T("Log in");
        ToolTip.SetTip(LogoutButton, user != null ? L.T("Lock the screen: on air carries on") : L.T("Nobody is logged in: the library couldn't be reached"));
        LastLog.Text = user != null ? L.F("{0} logged in", user.Name) : LastLog.Text;
        Playlist.Focus();
    }

    /// <summary>The users are in the library: log-in waits for it; if it can't be reached, the program opens.</summary>
    void LibraryForLogin()
    {
        if (!_locked) return;
        if (_library.Db is { } db)
        {
            LockLogin.Db = db;
            LockLogin.Reason = L.T("Log in to work on air.");
        }
        else if (_library.Error != null)
        {
            AppLog.Write("On air: open without a log-in (the library can't be reached)");
            LogIn(null);
            LastLog.Text = L.T("The library can't be reached: open without a log-in.");
        }
        else
        {
            LockLogin.Db = null;
            LockLogin.Reason = L.T("Opening the library…");
        }
    }
}
