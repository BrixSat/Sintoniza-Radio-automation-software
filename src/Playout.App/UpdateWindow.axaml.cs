using Avalonia.Controls;
using Playout.Engine;

namespace Playout.App;

/// <summary>
/// Shows a new release and installs it: downloads the package for this installation and runs it. Returns true when
/// the app should now close (Windows: the installer is waiting for it; Linux: restart into the new version).
/// </summary>
public partial class UpdateWindow : Window
{
    readonly UpdateInfo _update;
    readonly PlayoutEngine? _engine;
    CancellationTokenSource? _download;
    bool _installed; // Linux: installed, the button now restarts

    // Parameterless constructor for the XAML previewer.
    public UpdateWindow() : this(new UpdateInfo(new Version(1, 0, 0), "v1.0.0", "", Updates.ReleasesPage, null), null) { }

    public UpdateWindow(UpdateInfo update, PlayoutEngine? engine)
    {
        InitializeComponent();
        _update = update;
        _engine = engine;

        HeadingText.Text = $"{AppInfo.Name} {update.Version} is available";
        CurrentText.Text = $"You have version {Updates.CurrentText}.";
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? "No release notes." : update.Notes.Trim();

        if (update.Asset == null)
        {
            // Portable zip/tar or macOS: no installer this copy can run.
            InstallButton.Content = "Download page";
            ShowStatus("This copy can't update itself (it isn't installed with the setup or the .deb package). Download the new version from the release page.");
        }
        else if (Updates.InstallClosesApp)
            WarningText.Text = $"Installing closes {AppInfo.Name}, so playout and streaming stop until it starts again (about a minute). Update when you are off air.";
        else
            WarningText.Text = "Installing asks for the administrator password. Playout carries on; the new version runs after a restart.";
        WarningText.IsVisible = !string.IsNullOrEmpty(WarningText.Text);

        PageButton.Click += (_, _) => Updates.OpenInBrowser(update.PageUrl);
        LaterButton.Click += (_, _) => Close(false);
        InstallButton.Click += async (_, _) => await InstallClicked();
        Closing += (_, _) => _download?.Cancel();
    }

    bool OnAir => _engine is { } e && (e.State != PlayerState.Stopped || e.GetStatus().Stream is StreamState.OnAir or StreamState.Connecting);

    async Task InstallClicked()
    {
        if (_update.Asset is not { } asset) { Updates.OpenInBrowser(_update.PageUrl); return; }
        if (_installed) { await Restart(); return; }

        if (Updates.InstallClosesApp && OnAir
            && !await Dialogs.Confirm(this, "Stop playout and update?", $"{AppInfo.Name} is on air. Installing the update closes it, so playout and streaming stop.", "Update now"))
            return;

        InstallButton.IsEnabled = false;
        LaterButton.Content = "Cancel";
        _download = new CancellationTokenSource();
        try
        {
            ShowStatus($"Downloading {asset.Name}…");
            Progress.IsVisible = true;
            string file = await Updates.DownloadAsync(asset, new Progress<double>(p => Progress.Value = p), _download.Token);
            Progress.IsVisible = false;

            ShowStatus("Installing…");
            await Updates.InstallAsync(file);
            if (Updates.InstallClosesApp)
            {
                Close(true); // the installer waits for us to close, then starts the new version
                return;
            }
            _installed = true;
            ShowStatus($"Version {_update.Version} is installed. Restart {AppInfo.Name} to use it.");
            InstallButton.Content = "Restart now";
            InstallButton.IsEnabled = true;
            LaterButton.Content = "Restart later";
        }
        catch (OperationCanceledException ex) when (!(_download?.IsCancellationRequested ?? false))
        {
            Failed(ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Window closed during the download.
        }
        catch (Exception ex)
        {
            Failed(ex.Message);
        }
    }

    async Task Restart()
    {
        if (OnAir && !await Dialogs.Confirm(this, "Restart now?", $"{AppInfo.Name} is on air. Restarting stops playout and streaming for a few seconds.", "Restart"))
            return;
        Close(true);
    }

    void Failed(string message)
    {
        Progress.IsVisible = false;
        ShowStatus(message);
        StatusText.Foreground = (Avalonia.Media.IBrush)this.FindResource("OnAir")!;
        InstallButton.IsEnabled = true;
        InstallButton.Content = "Try again";
        LaterButton.Content = "Later";
    }

    void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = true;
        StatusText.ClearValue(TextBlock.ForegroundProperty);
    }
}
