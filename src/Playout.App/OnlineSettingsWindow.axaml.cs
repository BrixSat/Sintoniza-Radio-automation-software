using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>Sintoniza Online's settings: the source, the servers, reconnecting, listening, alerts, security. Closes with the new settings (null: cancelled).</summary>
public partial class OnlineSettingsWindow : Window
{
    static readonly int[] Bitrates = [32, 48, 64, 96, 128, 160, 192, 256, 320];

    readonly OnlineSettings _s = new();
    readonly List<string> _inputs = new(), _outputs = new();

    // Parameterless constructor for the XAML previewer.
    public OnlineSettingsWindow() : this(new OnlineSettings()) { }

    public OnlineSettingsWindow(OnlineSettings settings)
    {
        InitializeComponent();
        Localization.Apply(this);
        Title = L.F("{0} — Settings", AppInfo.OnlineName);
        _s = settings;

        // Source
        StationBox.Text = _s.StationName;
        try { _inputs.AddRange(OnlineStreamer.Inputs()); _outputs.AddRange(OnlineStreamer.Outputs()); }
        catch { /* no audio library: lists empty */ }
        InputBox.ItemsSource = new[] { L.T("(the default input)") }.Concat(_inputs).ToList();
        InputBox.SelectedIndex = Math.Max(0, _inputs.IndexOf(_s.Input) + 1);
        InputRadio.IsChecked = _s.Source == RecSource.Input;
        StreamRadio.IsChecked = _s.Source == RecSource.Stream;
        SourceUrlBox.Text = _s.SourceUrl;
        FormatBox.ItemsSource = new[] { "MP3", "Ogg Vorbis" };
        FormatBox.SelectedIndex = _s.Format == StreamFormat.Ogg ? 1 : 0;
        BitrateBox.ItemsSource = Bitrates.Select(b => $"{b} kbps").ToList();
        BitrateBox.SelectedIndex = Array.IndexOf(Bitrates, _s.Bitrate) is var at and >= 0 ? at : Array.IndexOf(Bitrates, 128);
        MonoBox.IsChecked = _s.Mono;
        StreamNameBox.Text = _s.StreamName;
        GenreBox.Text = _s.Genre;
        WebsiteBox.Text = _s.Website;
        DescriptionBox.Text = _s.Description;
        TitleBox.Text = _s.Title;
        PublicBox.IsChecked = _s.Public;

        // Servers
        ShowServer(_s.Server, IcecastRadio, ShoutcastRadio, HostBox, PortBox, MountBox, UserBox, PasswordBox);
        ShowServer(_s.Backup, BackupIcecastRadio, BackupShoutcastRadio, BackupHostBox, BackupPortBox, BackupMountBox, BackupUserBox, BackupPasswordBox);
        BackupBox.IsChecked = _s.BackupEnabled;
        BackupPanel.IsEnabled = _s.BackupEnabled;
        BackupBox.IsCheckedChanged += (_, _) => BackupPanel.IsEnabled = BackupBox.IsChecked == true;
        ShoutcastRadio.IsCheckedChanged += (_, _) => { MountBox.IsEnabled = UserBox.IsEnabled = ShoutcastRadio.IsChecked != true; };
        BackupShoutcastRadio.IsCheckedChanged += (_, _) => { BackupMountBox.IsEnabled = BackupUserBox.IsEnabled = BackupShoutcastRadio.IsChecked != true; };
        MountBox.IsEnabled = UserBox.IsEnabled = !_s.Server.Shoutcast;
        BackupMountBox.IsEnabled = BackupUserBox.IsEnabled = !_s.Backup.Shoutcast;

        // Reconnect
        RetryBox.Value = _s.RetrySeconds;
        BackupAfterBox.Value = _s.BackupAfter;
        FallbackBox.Text = _s.FallbackFile;
        FallbackSecondsBox.Value = (decimal)_s.FallbackSeconds;
        SilenceDbBox.Value = (decimal)_s.SilenceDb;
        FallbackButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("Fallback file"),
                FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }],
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) FallbackBox.Text = path;
        };

        // Listening
        OutputBox.ItemsSource = new[] { L.T("(the default output)") }.Concat(_outputs).ToList();
        OutputBox.SelectedIndex = Math.Max(0, _outputs.IndexOf(_s.MonitorOutput) + 1);

        // Alerts
        OfflineAlertBox.Value = _s.OfflineAlertSeconds;
        SilenceSecondsBox.Value = (decimal)_s.SilenceSeconds;
        AliveBox.IsChecked = _s.AliveHour != null;
        AliveHourBox.Value = _s.AliveHour ?? 9;
        var a = _s.Alerts;
        TelegramTokenBox.Text = a.TelegramToken;
        TelegramChatBox.Text = a.TelegramChat;
        SmtpServerBox.Text = a.SmtpServer;
        SmtpPortBox.Value = a.SmtpPort;
        SmtpSslBox.IsChecked = a.SmtpSsl;
        SmtpUserBox.Text = a.SmtpUser;
        SmtpPasswordBox.Text = a.SmtpPassword;
        MailFromBox.Text = a.MailFrom;
        MailToBox.Text = a.MailTo;
        WebhookBox.Text = a.WebhookUrl;
        TestAlertButton.Click += async (_, _) =>
        {
            var s = FromUi();
            if (!s.Alerts.HasChannel) { TestAlertText.Text = L.T("Fill in Telegram, e-mail or a web address first."); return; }
            TestAlertText.Text = L.T("Sending…");
            string problems = await AlertSender.SendAsync(s.Alerts, s.StationName.Length > 0 ? s.StationName : AppInfo.OnlineName, "test", true, L.T("test alert from Sintoniza Online"));
            TestAlertText.Text = problems.Length == 0 ? L.T("Sent.") : problems;
        };

        // Security
        StartOnlineBox.IsChecked = _s.StartOnline;
        AutostartBox.IsChecked = Autostart.Online.IsEnabled;
        AutostartBox.IsEnabled = Autostart.Online.IsSupported;
        LockBox.IsChecked = _s.IsLocked;
        LockPanel.IsEnabled = _s.IsLocked;
        LockBox.IsCheckedChanged += (_, _) => LockPanel.IsEnabled = LockBox.IsChecked == true;
        LockStateText.Text = _s.IsLocked ? L.T("A password is set: leave these empty to keep it.") : L.T("At least 4 characters.");
        LockMinutesBox.Value = _s.LockMinutes;

        CancelButton.Click += (_, _) => Close(null);
        SaveButton.Click += (_, _) => Save();
    }

    static void ShowServer(OnlineSettings.ServerSettings s, RadioButton icecast, RadioButton shoutcast, TextBox host, NumericUpDown port, TextBox mount, TextBox user, TextBox password)
    {
        icecast.IsChecked = !s.Shoutcast;
        shoutcast.IsChecked = s.Shoutcast;
        host.Text = s.Host;
        port.Value = s.Port;
        mount.Text = s.Mount;
        user.Text = s.User;
        password.Text = s.Password;
    }

    static OnlineSettings.ServerSettings ReadServer(RadioButton shoutcast, TextBox host, NumericUpDown port, TextBox mount, TextBox user, TextBox password)
    {
        // A whole address pasted (http://host:8000/live): its parts.
        string text = host.Text?.Trim() ?? "";
        var s = new OnlineSettings.ServerSettings
        {
            Host = text, Port = (int)(port.Value ?? 8000), Mount = mount.Text?.Trim() ?? "", User = user.Text?.Trim() ?? "",
            Password = password.Text ?? "", Shoutcast = shoutcast.IsChecked == true,
        };
        if (text.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            s.Host = uri.Host;
            s.Port = uri.Port; // http://host/live: 80
            if (uri.AbsolutePath.Trim('/').Length > 0) s.Mount = uri.AbsolutePath;
        }
        return s;
    }

    OnlineSettings FromUi()
    {
        var s = _s.Clone();
        s.StationName = StationBox.Text?.Trim() ?? "";
        s.Source = StreamRadio.IsChecked == true ? RecSource.Stream : RecSource.Input;
        s.Input = InputBox.SelectedIndex > 0 ? _inputs[InputBox.SelectedIndex - 1] : "";
        s.SourceUrl = SourceUrlBox.Text?.Trim() ?? "";
        s.Format = FormatBox.SelectedIndex == 1 ? StreamFormat.Ogg : StreamFormat.Mp3;
        s.Bitrate = Bitrates[Math.Max(0, BitrateBox.SelectedIndex)];
        s.Mono = MonoBox.IsChecked == true;
        s.StreamName = StreamNameBox.Text?.Trim() ?? "";
        s.Genre = GenreBox.Text?.Trim() ?? "";
        s.Website = WebsiteBox.Text?.Trim() ?? "";
        s.Description = DescriptionBox.Text?.Trim() ?? "";
        s.Title = TitleBox.Text?.Trim() ?? "";
        s.Public = PublicBox.IsChecked == true;

        s.Server = ReadServer(ShoutcastRadio, HostBox, PortBox, MountBox, UserBox, PasswordBox);
        s.Backup = ReadServer(BackupShoutcastRadio, BackupHostBox, BackupPortBox, BackupMountBox, BackupUserBox, BackupPasswordBox);
        s.BackupEnabled = BackupBox.IsChecked == true;

        s.RetrySeconds = (int)(RetryBox.Value ?? 5);
        s.BackupAfter = (int)(BackupAfterBox.Value ?? 2);
        s.FallbackFile = FallbackBox.Text?.Trim() ?? "";
        s.FallbackSeconds = (double)(FallbackSecondsBox.Value ?? 30);
        s.SilenceDb = (double)(SilenceDbBox.Value ?? -45);
        s.MonitorOutput = OutputBox.SelectedIndex > 0 ? _outputs[OutputBox.SelectedIndex - 1] : "";

        s.OfflineAlertSeconds = (int)(OfflineAlertBox.Value ?? 30);
        s.SilenceSeconds = (double)(SilenceSecondsBox.Value ?? 60);
        s.AliveHour = AliveBox.IsChecked == true ? (int)(AliveHourBox.Value ?? 9) : null;
        s.Alerts = new AlertSettings
        {
            Enabled = true,
            TelegramToken = TelegramTokenBox.Text?.Trim() ?? "", TelegramChat = TelegramChatBox.Text?.Trim() ?? "",
            SmtpServer = SmtpServerBox.Text?.Trim() ?? "", SmtpPort = (int)(SmtpPortBox.Value ?? 587), SmtpSsl = SmtpSslBox.IsChecked == true,
            SmtpUser = SmtpUserBox.Text?.Trim() ?? "", SmtpPassword = SmtpPasswordBox.Text ?? "", MailFrom = MailFromBox.Text?.Trim() ?? "",
            MailTo = MailToBox.Text?.Trim() ?? "", WebhookUrl = WebhookBox.Text?.Trim() ?? "",
        };

        s.StartOnline = StartOnlineBox.IsChecked == true;
        s.LockMinutes = (int)(LockMinutesBox.Value ?? 5);
        return s;
    }

    void Save()
    {
        var s = FromUi();
        string? error = null;
        if (s.Source == RecSource.Stream && !(Uri.TryCreate(s.SourceUrl, UriKind.Absolute, out var source) && source.Scheme is "http" or "https"))
            error = L.T("Write the address of the stream to relay (http://…).");
        else if (s.Server.Host.Length == 0) error = L.T("Write the server's address (Server).");
        else if (s.Server.Host.Any(char.IsWhiteSpace) || s.Server.Host.Contains('/')) error = L.T("The server's address is only its name or IP, like stream.example.com (the port and mount go in their own boxes).");
        else if (s.Server.Password.Length == 0) error = L.T("Write the server's password (Server).");
        else if (s.BackupEnabled && (s.Backup.Host.Length == 0 || s.Backup.Password.Length == 0)) error = L.T("Write the backup server's address and password, or untick it.");
        else if (s.BackupEnabled && (s.Backup.Host.Any(char.IsWhiteSpace) || s.Backup.Host.Contains('/'))) error = L.T("The server's address is only its name or IP, like stream.example.com (the port and mount go in their own boxes).");
        else if (s.BackupEnabled && s.Backup.ToServer().Address == s.Server.ToServer().Address) error = L.T("The backup server is the main one: give another server or mount.");
        else if (s.FallbackFile.Length > 0 && !File.Exists(SharedPaths.ToLocal(s.FallbackFile))) error = L.T("The fallback file isn't there.");
        if (error == null)
        {
            string password = LockPasswordBox.Text ?? "", repeat = LockRepeatBox.Text ?? "";
            if (LockBox.IsChecked != true) s.LockHash = "";
            else if (password.Length == 0 && repeat.Length == 0 && _s.IsLocked) { } // kept
            else if (password.Length < 4) error = L.T("The lock password needs at least 4 characters.");
            else if (password != repeat) error = L.T("The two lock passwords aren't the same.");
            else s.LockHash = UserStore.Hash(password);
        }
        if (error != null)
        {
            ErrorText.Text = error;
            return;
        }
        try { if (Autostart.Online.IsSupported) Autostart.Online.Set(AutostartBox.IsChecked == true); }
        catch (Exception ex) { ErrorText.Text = ex.Message; return; }
        Close(s);
    }
}
