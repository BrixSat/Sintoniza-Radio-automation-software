using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>Sintoniza Rec's settings: where it records from, the files, listening, alerts. Closes with the new settings (null: cancelled).</summary>
public partial class RecSettingsWindow : Window
{
    static readonly int[] Bitrates = [64, 96, 128, 160, 192, 256, 320];
    static readonly int[] ArchiveBitrates = [16, 24, 32, 48, 64];
    static readonly int[] Minutes = [5, 10, 15, 20, 30, 60];

    readonly RecSettings _s = new();
    readonly List<string> _inputs = new(), _outputs = new();

    // Parameterless constructor for the XAML previewer.
    public RecSettingsWindow() : this(new RecSettings()) { }

    public RecSettingsWindow(RecSettings settings)
    {
        InitializeComponent();
        Localization.Apply(this);
        Title = L.F("{0} — Settings", AppInfo.RecName);
        _s = settings;

        StationBox.Text = _s.StationName;
        try { _inputs.AddRange(StationRecorder.Inputs()); _outputs.AddRange(StationRecorder.Outputs()); }
        catch { /* no audio library: lists empty */ }
        InputBox.ItemsSource = new[] { L.T("(the default input)") }.Concat(_inputs).ToList();
        InputBox.SelectedIndex = Math.Max(0, _inputs.IndexOf(_s.Input) + 1);
        OutputBox.ItemsSource = new[] { L.T("(the default output)") }.Concat(_outputs).ToList();
        OutputBox.SelectedIndex = Math.Max(0, _outputs.IndexOf(_s.MonitorOutput) + 1);
        InputRadio.IsChecked = _s.Source == RecSource.Input;
        StreamRadio.IsChecked = _s.Source == RecSource.Stream;
        StreamBox.Text = _s.StreamUrl;
        AutostartBox.IsChecked = Autostart.Rec.IsEnabled;
        AutostartBox.IsEnabled = Autostart.Rec.IsSupported;

        FolderBox.Text = _s.Folder;
        PrefixBox.Text = _s.Prefix;
        FormatBox.ItemsSource = new[] { "MP3", L.T("WAV (no compression: about 10 GB a day)") };
        FormatBox.SelectedIndex = _s.Format == RecFormat.Wav ? 1 : 0;
        BitrateBox.ItemsSource = Bitrates.Select(b => $"{b} kbps").ToList();
        BitrateBox.SelectedIndex = Math.Max(0, Array.IndexOf(Bitrates, _s.Bitrate));
        MonoBox.IsChecked = _s.Mono;
        MinutesBox.ItemsSource = Minutes.Select(m => L.F("{0} minutes", m)).ToList();
        int minutes = Array.IndexOf(Minutes, _s.FileMinutes);
        MinutesBox.SelectedIndex = minutes >= 0 ? minutes : Minutes.Length - 1;
        StartMinuteBox.Value = _s.StartMinute;
        KeepDaysBox.Value = _s.KeepDays;
        ArchiveBox.IsChecked = _s.Archive;
        ArchiveFolderBox.Text = _s.ArchiveFolder;
        ArchiveBitrateBox.ItemsSource = ArchiveBitrates.Select(b => $"{b} kbps").ToList();
        int archive = Array.IndexOf(ArchiveBitrates, _s.ArchiveBitrate);
        ArchiveBitrateBox.SelectedIndex = archive >= 0 ? archive : 2;
        ArchiveMonthsBox.Value = _s.ArchiveKeepMonths;

        SilenceDbBox.Value = (decimal)_s.SilenceDb;
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

        foreach (var c in new Control[] { FormatBox, BitrateBox, MonoBox, KeepDaysBox })
            c.PropertyChanged += (_, e) => { if (e.Property.Name is "SelectedIndex" or "IsChecked" or "Value") ShowSpace(); };
        ShowSpace();
        FolderButton.Click += async (_, _) => { if (await Pick(L.T("Folder for the recordings")) is { } f) FolderBox.Text = f; };
        ArchiveFolderButton.Click += async (_, _) => { if (await Pick(L.T("Folder for the archive")) is { } f) ArchiveFolderBox.Text = f; };
        TestAlertButton.Click += async (_, _) =>
        {
            var s = FromUi();
            if (!s.Alerts.HasChannel) { TestAlertText.Text = L.T("Fill in Telegram, e-mail or a web address first."); return; }
            TestAlertText.Text = L.T("Sending…");
            string problems = await AlertSender.SendAsync(s.Alerts, s.StationName.Length > 0 ? s.StationName : AppInfo.RecName, "test", true, L.T("test alert from Sintoniza Rec"));
            TestAlertText.Text = problems.Length == 0 ? L.T("Sent.") : problems;
        };
        CancelButton.Click += (_, _) => Close(null);
        SaveButton.Click += (_, _) => Save();
    }

    async Task<string?> Pick(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>About how much space a day and the whole kept period take.</summary>
    void ShowSpace()
    {
        bool wav = FormatBox.SelectedIndex == 1;
        double kbps = wav ? 44100 * 16 * (MonoBox.IsChecked == true ? 1 : 2) / 1000.0 : Bitrates[Math.Max(0, BitrateBox.SelectedIndex)];
        double dayGb = kbps * 1000 / 8 * 86400 / 1e9;
        int days = (int)(KeepDaysBox.Value ?? 45);
        SpaceText.Text = L.F("About {0:0.0} GB a day, {1:0} GB for the {2} days kept.", dayGb, dayGb * days, days)
                         + (days < 30 ? "  " + L.T("Less than the 30 days the law asks for.") : "");
    }

    RecSettings FromUi()
    {
        var s = _s.Clone();
        s.StationName = StationBox.Text?.Trim() ?? "";
        s.Source = StreamRadio.IsChecked == true ? RecSource.Stream : RecSource.Input;
        s.Input = InputBox.SelectedIndex > 0 ? _inputs[InputBox.SelectedIndex - 1] : "";
        s.StreamUrl = StreamBox.Text?.Trim() ?? "";
        s.Folder = FolderBox.Text?.Trim() ?? "";
        s.Prefix = string.Concat((PrefixBox.Text ?? "").Split(Path.GetInvalidFileNameChars()));
        s.Format = FormatBox.SelectedIndex == 1 ? RecFormat.Wav : RecFormat.Mp3;
        s.Bitrate = Bitrates[Math.Max(0, BitrateBox.SelectedIndex)];
        s.Mono = MonoBox.IsChecked == true;
        s.FileMinutes = Minutes[Math.Max(0, MinutesBox.SelectedIndex)];
        s.StartMinute = (int)(StartMinuteBox.Value ?? 0);
        s.KeepDays = (int)(KeepDaysBox.Value ?? 45);
        s.Archive = ArchiveBox.IsChecked == true;
        s.ArchiveFolder = ArchiveFolderBox.Text?.Trim() ?? "";
        s.ArchiveBitrate = ArchiveBitrates[Math.Max(0, ArchiveBitrateBox.SelectedIndex)];
        s.ArchiveKeepMonths = (int)(ArchiveMonthsBox.Value ?? 0);
        s.MonitorOutput = OutputBox.SelectedIndex > 0 ? _outputs[OutputBox.SelectedIndex - 1] : "";
        s.SilenceDb = (double)(SilenceDbBox.Value ?? -45);
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
        return s;
    }

    void Save()
    {
        var s = FromUi();
        if (s.Source == RecSource.Stream && !Uri.TryCreate(s.StreamUrl, UriKind.Absolute, out _))
        {
            ErrorText.Text = L.T("Write the stream's address (http://…).");
            return;
        }
        if (s.Folder.Length == 0)
        {
            ErrorText.Text = L.T("Choose the folder for the recordings.");
            return;
        }
        if (s.Archive && s.ArchiveFolder.Length == 0)
        {
            ErrorText.Text = L.T("Choose the folder for the archive (or untick it).");
            return;
        }
        try { if (Autostart.Rec.IsSupported) Autostart.Rec.Set(AutostartBox.IsChecked == true); }
        catch (Exception ex) { ErrorText.Text = ex.Message; return; }
        Close(s);
    }
}
