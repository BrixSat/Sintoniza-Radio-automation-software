using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Playout.Engine;
using Playout.Library;

namespace Playout.App;

/// <summary>Station, playout and streaming settings. Edits a copy; <see cref="AppSettings"/> is only changed on Save.</summary>
public partial class SettingsWindow : Window
{
    static readonly string DefaultDevice = L.T("System default");

    readonly AppSettings _settings;
    readonly PlayoutEngine? _engine;
    readonly MainWindow? _main;
    UpdateInfo? _update;
    string? _logoPath;
    string? _signalFile;
    string _hourFolder = "";
    string _fillFolder = "";
    string _libraryFile = "";
    readonly System.Collections.ObjectModel.ObservableCollection<string> _libraryFolders = new();
    readonly Dictionary<string, ShortcutBox> _keyBoxes = new();

    // Parameterless constructor for the XAML previewer.
    public SettingsWindow() : this(new AppSettings(), null) { }

    public SettingsWindow(AppSettings settings, PlayoutEngine? engine, MainWindow? main = null)
    {
        InitializeComponent();
        Localization.Apply(this);
        _settings = settings;
        _engine = engine;
        _main = main;

        VersionText.Text = Updates.IsDevBuild ? L.T("Development build (built from source)") : L.F("Version {0}", Updates.CurrentText);
        AutoUpdateBox.IsChecked = settings.CheckForUpdates;
        WebsiteButton.Click += (_, _) => Updates.OpenInBrowser("https://cesararaujo.net");
        SourceButton.Click += (_, _) => Updates.OpenInBrowser(Updates.ReleasesPage);
        CheckNowButton.Click += async (_, _) => await CheckNow();
        CheckNowButton.IsEnabled = main != null;
        ShowUpdateButton.Click += async (_, _) => { if (_update != null && _main != null) await _main.ShowUpdate(_update, this); };

        StationNameBox.Text = settings.StationName;
        GenreBox.Text = settings.StationGenre;
        UrlBox.Text = settings.StationUrl;
        CurrencyBox.Text = settings.CurrencySign;
        LanguageBox.ItemsSource = Localization.Languages.Select(l => l.Code == "" ? L.T(l.Name) : l.Name).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Localization.Languages, l => l.Code == settings.Language));
        CurrencyBox.PlaceholderText = PlaylistRow.SystemCurrency;
        SetLogo(settings.LogoPath);

        StationTypeBox.SelectedIndex = settings.StationType == StationType.Web ? 1 : 0;
        StationTypeBox.SelectionChanged += (_, _) => ShowMicPanel();
        var mic = settings.Mic ?? new();
        var inputs = PlayoutEngine.GetInputDevices().Prepend(DefaultDevice).ToList();
        MicDeviceBox.ItemsSource = inputs;
        MicDeviceBox.SelectedItem = inputs.Contains(mic.Device) ? mic.Device : DefaultDevice;
        MicGainBox.Value = (decimal)mic.GainDb;
        DuckBox.Value = (decimal)mic.DuckDb;
        MicToggleRadio.IsChecked = settings.MicToggle;
        MicHoldRadio.IsChecked = !settings.MicToggle;
        FadeDownBox.Value = (decimal)mic.FadeDown;
        FadeUpBox.Value = (decimal)mic.FadeUp;
        ShowMicPanel();

        var signal = settings.TimeSignal ?? new();
        TimeSignalModeBox.SelectedIndex = (int)signal.Mode;
        _signalFile = string.IsNullOrEmpty(signal.File) ? null : signal.File;
        SignalFileRadio.IsChecked = _signalFile != null;
        PipsRadio.IsChecked = _signalFile == null;
        ShowSignalFile();
        ChooseSignalButton.Click += async (_, _) => await ChooseSignalFile();
        _hourFolder = signal.HourFolder ?? "";
        HourAtBox.SelectedIndex = (int)signal.HourAt;
        ShowHourFolder();
        ChooseHourFolderButton.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Folder with the hour announcements") });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            _hourFolder = path;
            ShowHourFolder();
        };
        ClearHourFolderButton.Click += (_, _) =>
        {
            _hourFolder = "";
            ShowHourFolder();
        };
        ListenSignalButton.Click += (_, _) => _engine?.PreviewTimeSignal(SignalFromUi());
        ListenSignalButton.IsEnabled = _engine != null;

        var silence = settings.Silence ?? new();
        SilenceBox.IsChecked = silence.Enabled;
        SilenceLevelBox.Value = (decimal)silence.ThresholdDb;
        SilenceSecondsBox.Value = (decimal)silence.Seconds;
        SilencePanel.IsEnabled = silence.Enabled;
        SilenceBox.IsCheckedChanged += (_, _) => SilencePanel.IsEnabled = SilenceBox.IsChecked == true;
        FillBox.IsChecked = settings.FillEnabled;
        _fillFolder = settings.FillFolder ?? "";
        FillLibraryRadio.IsChecked = settings.FillSource == FillSource.Library;
        FillFolderRadio.IsChecked = settings.FillSource == FillSource.Folder;
        FillSongHoursBox.Value = (decimal)settings.FillSongHours;
        FillArtistMinutesBox.Value = settings.FillArtistMinutes;
        ShowFillFolder();
        FillBox.IsCheckedChanged += (_, _) => ShowFillFolder();
        FillLibraryRadio.IsCheckedChanged += (_, _) => ShowFillFolder();
        ChooseFillButton.Click += async (_, _) => await ChooseFillFolder();
        ClearFillButton.Click += (_, _) => { _fillFolder = ""; ShowFillFolder(); };
        _ = LoadFillCategories(settings.FillCategories ?? new());
        ResumeBox.IsChecked = settings.ResumeOnStart;
        WatchdogBox.IsChecked = settings.RestartAfterCrash;
        AutostartBox.IsChecked = Autostart.IsEnabled;
        AutostartBox.IsEnabled = Autostart.IsSupported;
        if (!Autostart.IsSupported) ToolTip.SetTip(AutostartBox, L.T("Only in an installed copy of the app"));
        OpenLogsButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppLog.Folder);
            Updates.OpenInBrowser(AppLog.Folder);
        };

        var library = settings.Library ?? new();
        LibraryTypeBox.SelectedIndex = library.IsMySql ? 1 : 0;
        LibraryTypeBox.SelectionChanged += (_, _) => ShowLibraryType();
        _libraryFile = library.File;
        DbServerBox.Text = library.Server;
        DbPortBox.Text = library.Port.ToString();
        DbNameBox.Text = library.Database;
        DbUserBox.Text = library.User;
        DbPasswordBox.Text = library.Password;
        foreach (var f in library.Folders) _libraryFolders.Add(f);
        AutoScanBox.IsChecked = library.ScanEveryMinutes > 0;
        AutoScanMinutesBox.Value = library.ScanEveryMinutes > 0 ? library.ScanEveryMinutes : 60;
        AutoScanMinutesBox.IsEnabled = library.ScanEveryMinutes > 0;
        AutoScanBox.IsCheckedChanged += (_, _) => AutoScanMinutesBox.IsEnabled = AutoScanBox.IsChecked == true;
        WatchFoldersBox.IsChecked = library.WatchFolders;
        LibraryFoldersList.ItemsSource = _libraryFolders;
        ShowLibraryType();
        ChooseLibraryFileButton.Click += async (_, _) => await ChooseLibraryFile();
        TestLibraryButton.Click += async (_, _) => await TestLibrary();
        AddLibraryFolderButton.Click += async (_, _) => await AddLibraryFolder();
        RemoveLibraryFolderButton.Click += (_, _) =>
        {
            foreach (var f in LibraryFoldersList.SelectedItems?.OfType<string>().ToList() ?? []) _libraryFolders.Remove(f);
        };

        LoadLoudness(settings.Loudness ?? new());
        LoadWeb(settings.WebNowPlaying ?? new());
        LoadRds(settings.Rds ?? new());

        BuildShortcuts(settings.Shortcuts ?? Shortcuts.Defaults());
        DefaultKeysButton.Click += (_, _) =>
        {
            var defaults = Shortcuts.Defaults();
            foreach (var (id, box) in _keyBoxes) box.Gesture = Shortcuts.Parse(defaults.GetValueOrDefault(id));
            ShortcutMessage.Text = "";
        };

        CrossfadeBox.Value = (decimal)settings.Crossfade;
        OtherCrossfadeBox.Value = (decimal)settings.OtherCrossfade;
        _colors = new PlaylistColors
        {
            AdBreak = settings.PlaylistColors?.AdBreak, Jingle = settings.PlaylistColors?.Jingle, Programme = settings.PlaylistColors?.Programme,
            Stream = settings.PlaylistColors?.Stream, Music = settings.PlaylistColors?.Music, OnAir = settings.PlaylistColors?.OnAir,
        };
        BuildColourRows();
        var devices = PlayoutEngine.GetOutputDevices().Prepend(DefaultDevice).ToList();
        SetupOutput(MusicDeviceBox, MusicChannelBox, MusicTestButton, devices, settings.MusicOutput ?? new());
        SetupOutput(CartDeviceBox, CartChannelBox, CartTestButton, devices, settings.CartOutput ?? new());
        SetupOutput(PreviewDeviceBox, PreviewChannelBox, PreviewTestButton, devices, settings.PreviewOutput ?? new());

        ServerTypeBox.SelectedIndex = settings.Shoutcast ? 1 : 0;
        HostBox.Text = settings.StreamHost;
        PortBox.Text = settings.StreamPort.ToString();
        MountBox.Text = settings.StreamMount;
        PasswordBox.Text = settings.StreamPassword;
        FormatBox.SelectedIndex = settings.StreamFormat;
        BitrateBox.SelectedIndex = settings.StreamBitrateIndex;
        MountPanel.IsEnabled = !settings.Shoutcast;
        ServerTypeBox.SelectionChanged += (_, _) => MountPanel.IsEnabled = ServerTypeBox.SelectedIndex == 0;

        ChooseLogoButton.Click += async (_, _) => await ChooseLogo();
        RemoveLogoButton.Click += (_, _) => SetLogo(null);
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += (_, _) => Save();
    }

    // ───────────────────────── Outputs ─────────────────────────

    void SetupOutput(ComboBox deviceBox, ComboBox channelBox, Button test, List<string> devices, OutputRoute route)
    {
        deviceBox.ItemsSource = devices;
        deviceBox.SelectedItem = devices.Contains(route.Device) ? route.Device : DefaultDevice;
        FillChannels(deviceBox, channelBox, route.FirstChannel);
        deviceBox.SelectionChanged += (_, _) => FillChannels(deviceBox, channelBox, 0);
        test.Click += (_, _) =>
        {
            try { _engine?.PlayTestTone(RouteFrom(deviceBox, channelBox)); }
            catch (Exception ex) { ToolTip.SetTip(test, ex.Message); }
        };
        test.IsEnabled = _engine != null;
    }

    /// <summary>Lists the stereo pairs the selected card offers: "Out 1–2", "Out 3–4", …</summary>
    void FillChannels(ComboBox deviceBox, ComboBox channelBox, int firstChannel)
    {
        int channels = 2;
        try { channels = _engine?.GetOutputChannelCount(DeviceName(deviceBox)) ?? 2; }
        catch { /* card unavailable: offer the first pair only */ }
        var pairs = Enumerable.Range(0, Math.Max(1, channels / 2)).Select(p => L.F("Out {0}–{1}", p * 2 + 1, p * 2 + 2)).ToList();
        channelBox.ItemsSource = pairs;
        channelBox.SelectedIndex = Math.Clamp(firstChannel / 2, 0, pairs.Count - 1);
        channelBox.IsEnabled = pairs.Count > 1;
    }

    static string DeviceName(ComboBox deviceBox) =>
        deviceBox.SelectedItem as string is { } d && d != DefaultDevice ? d : "";

    static OutputRoute RouteFrom(ComboBox deviceBox, ComboBox channelBox) =>
        new(DeviceName(deviceBox), Math.Max(0, channelBox.SelectedIndex) * 2);

    // ───────────────────────── Updates ─────────────────────────

    async Task CheckNow()
    {
        CheckNowButton.IsEnabled = false;
        UpdateResultText.Text = L.T("Checking…");
        ShowUpdateButton.IsVisible = false;
        try
        {
            _update = await _main!.CheckForUpdate(quiet: false);
            UpdateResultText.Text = _update != null ? L.F("Version {0} is available.", _update.Version)
                : Updates.IsDevBuild ? L.T("Built from source: no version to compare. The latest release is on GitHub.")
                : L.T("You have the latest version.");
            ShowUpdateButton.IsVisible = _update != null;
        }
        catch (Exception ex)
        {
            UpdateResultText.Text = L.F("Could not check: {0}", ex.Message);
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    // ───────────────────────── Shortcuts ─────────────────────────

    /// <summary>One row per action: transport and mic on the left, carts 1–6 and 7–12 beside them.</summary>
    void BuildShortcuts(Dictionary<string, string> keys)
    {
        var carts = Enumerable.Range(0, AppSettings.CartCount).Select(Shortcuts.Cart).ToList();
        foreach (var (id, name) in Shortcuts.Actions)
        {
            var box = new ShortcutBox { Gesture = Shortcuts.Parse(keys.GetValueOrDefault(id)) };
            box.Assigning = g => Assign(id, g);
            _keyBoxes[id] = box;

            int cart = carts.IndexOf(id);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(cart < 0 ? "110,*" : "56,*") };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            (cart < 0 ? PlayerKeys : cart < carts.Count / 2 ? CartKeysA : CartKeysB).Children.Add(row);
        }
    }

    /// <summary>Refuses keys the app already uses; a key used by another action moves here.</summary>
    bool Assign(string id, KeyGesture? g)
    {
        ShortcutMessage.Text = "";
        if (g == null) return true;
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (Shortcuts.Reserved(g, command) is { } use)
        {
            ShortcutMessage.Text = L.F("{0} is already used for {1}.", Shortcuts.Display(g), use);
            return false;
        }
        foreach (var (other, box) in _keyBoxes)
        {
            if (other == id || !g.Equals(box.Gesture)) continue;
            box.Gesture = null;
            ShortcutMessage.Text = L.F("{0} was moved from {1} to {2}.", Shortcuts.Display(g), Shortcuts.Name(other), Shortcuts.Name(id));
        }
        return true;
    }

    void ShowMicPanel()
    {
        bool web = StationTypeBox.SelectedIndex == 1;
        MicPanel.IsEnabled = web;
        MicFmNote.IsVisible = !web;
    }

    // ───────────────────────── Library ─────────────────────────

    void ShowLibraryType()
    {
        bool mySql = LibraryTypeBox.SelectedIndex == 1;
        SqlitePanel.IsVisible = !mySql;
        MySqlPanel.IsVisible = mySql;
        LibraryFileText.Text = string.IsNullOrWhiteSpace(_libraryFile) ? L.F("{0} (default)", LibraryDb.DefaultFile) : _libraryFile;
        LibraryTestText.Text = "";
    }

    LibrarySettings LibraryFromUi() => new()
    {
        Provider = LibraryTypeBox.SelectedIndex == 1 ? "mysql" : "sqlite",
        File = _libraryFile,
        Server = DbServerBox.Text?.Trim() is { Length: > 0 } server ? server : "localhost",
        Port = int.TryParse(DbPortBox.Text, out int port) && port > 0 ? port : 3306,
        Database = DbNameBox.Text?.Trim() ?? "",
        User = DbUserBox.Text?.Trim() ?? "",
        Password = DbPasswordBox.Text ?? "",
        Folders = _libraryFolders.ToList(),
        ScanEveryMinutes = AutoScanBox.IsChecked == true ? (int)(AutoScanMinutesBox.Value ?? 60) : 0,
        WatchFolders = WatchFoldersBox.IsChecked == true,
        FolderCategories = new(_settings.Library?.FolderCategories ?? new()), // set from the Folders tab
    };

    async Task TestLibrary()
    {
        var library = LibraryFromUi();
        TestLibraryButton.IsEnabled = false;
        LibraryTestText.Text = L.T("Connecting…");
        try
        {
            var stats = await Task.Run(() => LibraryDb.Open(library).Stats());
            LibraryTestText.Text = L.F("Works: {0:N0} songs in {1}", stats.Tracks, library.Describe());
        }
        catch (Exception ex)
        {
            LibraryTestText.Text = L.F("Failed: {0}", ex.Message);
        }
        finally
        {
            TestLibraryButton.IsEnabled = true;
        }
    }

    async Task ChooseLibraryFile()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("Library database file (an existing one, or a new one)"),
            SuggestedFileName = "library.db",
            DefaultExtension = "db",
            ShowOverwritePrompt = false,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _libraryFile = path;
        ShowLibraryType();
    }

    async Task AddLibraryFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("Add a music folder to the library"),
            AllowMultiple = true,
        });
        foreach (var f in folders)
            if (f.TryGetLocalPath() is { } path && !_libraryFolders.Contains(path)) _libraryFolders.Add(path);
    }

    // ───────────────────────── RDS ─────────────────────────

    static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];
    string _rdsFile = "";

    void LoadRds(RdsSettings rds)
    {
        RdsBox.IsChecked = rds.Enabled;
        RdsPsBox.Text = rds.Ps;
        RdsTemplateBox.Text = rds.Template;
        RdsDefaultBox.Text = rds.DefaultText;
        RdsJinglesBox.IsChecked = rds.DefaultForJingles;
        RdsProtocolBox.SelectedIndex = (int)rds.Protocol;
        RdsConnectionBox.SelectedIndex = (int)rds.Connection;
        RdsHostBox.Text = rds.Host;
        RdsPortBox.Text = rds.Port > 0 ? rds.Port.ToString() : "";
        var ports = SerialPortNames();
        if (rds.SerialPort.Length > 0 && !ports.Contains(rds.SerialPort)) ports.Insert(0, rds.SerialPort); // unplugged now
        RdsSerialBox.ItemsSource = ports;
        RdsSerialBox.SelectedItem = rds.SerialPort.Length > 0 ? rds.SerialPort : ports.FirstOrDefault();
        RdsBaudBox.ItemsSource = BaudRates;
        RdsBaudBox.SelectedItem = BaudRates.Contains(rds.BaudRate) ? rds.BaudRate : 9600;
        _rdsFile = rds.FilePath;
        RdsPsCommandBox.Text = rds.PsCommand;
        RdsRtCommandBox.Text = rds.RtCommand;
        RdsCharsetBox.SelectedIndex = (int)rds.Charset;
        RdsRtPlusBox.IsChecked = rds.RtPlus;
        RdsRtPlusCommandBox.Text = rds.RtPlusCommand;
        RdsRtPlusBox.IsCheckedChanged += (_, _) => ShowRds();
        RdsSiteBox.Value = rds.SiteAddress;
        RdsEncoderBox.Value = rds.EncoderAddress;
        RdsResendBox.Value = rds.ResendSeconds;
        ShowRds();
        RdsBox.IsCheckedChanged += (_, _) => ShowRds();
        RdsProtocolBox.SelectionChanged += (_, _) => ShowRds();
        RdsConnectionBox.SelectionChanged += (_, _) => ShowRds();
        ChooseRdsFileButton.Click += async (_, _) => await ChooseRdsFile();
        RdsTestButton.Click += async (_, _) => await TestRds();
    }

    static List<string> SerialPortNames()
    {
        try { return System.IO.Ports.SerialPort.GetPortNames().Order().ToList(); }
        catch { return new(); }
    }

    void ShowRds()
    {
        RdsPanel.IsEnabled = RdsBox.IsChecked == true;
        var protocol = (RdsProtocol)Math.Max(0, RdsProtocolBox.SelectedIndex);
        var connection = (RdsConnection)Math.Max(0, RdsConnectionBox.SelectedIndex);
        bool file = protocol == RdsProtocol.TextFile;
        RdsConnectionPanel.IsVisible = !file;
        RdsNetworkPanel.IsVisible = !file && connection != RdsConnection.Serial;
        RdsSerialPanel.IsVisible = !file && connection == RdsConnection.Serial;
        RdsFilePanel.IsVisible = file;
        RdsAsciiPanel.IsVisible = protocol == RdsProtocol.Ascii;
        RdsRtPlusPanel.IsVisible = protocol == RdsProtocol.Ascii;
        RdsRtPlusCommandBox.IsEnabled = RdsRtPlusBox.IsChecked == true;
        RdsUecpPanel.IsVisible = protocol == RdsProtocol.Uecp;
        RdsFileText.Text = _rdsFile.Length > 0 ? _rdsFile : L.T("No file chosen");
        ToolTip.SetTip(RdsFileText, _rdsFile.Length > 0 ? _rdsFile : null);
    }

    RdsSettings RdsFromUi() => new()
    {
        Enabled = RdsBox.IsChecked == true,
        Ps = RdsPsBox.Text ?? "",
        Template = RdsTemplateBox.Text?.Trim() is { Length: > 0 } template ? template : "{artist} - {title}",
        DefaultText = RdsDefaultBox.Text?.Trim() ?? "",
        DefaultForJingles = RdsJinglesBox.IsChecked == true,
        Protocol = (RdsProtocol)Math.Max(0, RdsProtocolBox.SelectedIndex),
        Connection = (RdsConnection)Math.Max(0, RdsConnectionBox.SelectedIndex),
        Host = RdsHostBox.Text?.Trim() ?? "",
        Port = int.TryParse(RdsPortBox.Text, out int port) && port is > 0 and < 65536 ? port : 0,
        SerialPort = RdsSerialBox.SelectedItem as string ?? "",
        BaudRate = RdsBaudBox.SelectedItem is int baud ? baud : 9600,
        FilePath = _rdsFile,
        PsCommand = RdsPsCommandBox.Text ?? "",
        RtCommand = RdsRtCommandBox.Text ?? "",
        RtPlus = RdsRtPlusBox.IsChecked == true,
        RtPlusCommand = RdsRtPlusCommandBox.Text?.Trim() is { Length: > 0 } rtPlus ? rtPlus : new RdsSettings().RtPlusCommand,
        Charset = (RdsCharset)Math.Max(0, RdsCharsetBox.SelectedIndex),
        SiteAddress = (int)(RdsSiteBox.Value ?? 0),
        EncoderAddress = (int)(RdsEncoderBox.Value ?? 0),
        ResendSeconds = (int)(RdsResendBox.Value ?? 60),
    };

    async Task ChooseRdsFile()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L.T("File to write the radio text to"),
            SuggestedFileName = "nowplaying.txt",
            DefaultExtension = "txt",
            ShowOverwritePrompt = false,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _rdsFile = path;
        ShowRds();
    }

    /// <summary>Sends the PS and radio text for what is on air now, with the settings on screen.</summary>
    async Task TestRds()
    {
        var rds = RdsFromUi();
        string station = StationNameBox.Text?.Trim() is { Length: > 0 } name ? name : AppInfo.Name;
        var status = _engine?.GetStatus();
        var onAir = status is { State: not PlayerState.Stopped } st ? st.Current : null;
        var (ps, rt) = RdsText.Compose(rds, onAir, station);
        RdsTestButton.IsEnabled = false;
        RdsTestText.Text = L.T("Sending…");
        using var output = new RdsOutput(rds, station);
        output.Update(onAir);
        string? error = await output.SendNowAsync();
        RdsTestText.Text = error == null ? L.F("Sent: PS \"{0}\", RT \"{1}\"", ps, rt) : L.F("Failed: {0}", error);
        RdsTestButton.IsEnabled = true;
    }

    // ───────────────────────── Loudness ─────────────────────────

    void LoadLoudness(LoudnessSettings loudness)
    {
        LoudnessBox.IsChecked = loudness.Enabled;
        LoudnessTargetBox.Value = (decimal)loudness.TargetLufs;
        LoudnessBoostBox.Value = (decimal)loudness.MaxBoostDb;
        LoudnessPanel.IsEnabled = loudness.Enabled;
        LoudnessBox.IsCheckedChanged += (_, _) => LoudnessPanel.IsEnabled = LoudnessBox.IsChecked == true;
    }

    LoudnessSettings LoudnessFromUi() => new(LoudnessBox.IsChecked == true,
        (double)(LoudnessTargetBox.Value ?? -16), (double)(LoudnessBoostBox.Value ?? 6));

    // ───────────────────────── Website ─────────────────────────

    void LoadWeb(WebNowPlayingSettings web)
    {
        WebBox.IsChecked = web.Enabled;
        WebUrlBox.Text = web.Url;
        WebKeyBox.Text = web.Key;
        WebSongsOnlyBox.IsChecked = web.SongsOnly;
        WebPanel.IsEnabled = web.Enabled;
        WebBox.IsCheckedChanged += (_, _) => WebPanel.IsEnabled = WebBox.IsChecked == true;
        WebTestButton.Click += async (_, _) => await TestWeb();
    }

    WebNowPlayingSettings WebFromUi() => new()
    {
        Enabled = WebBox.IsChecked == true,
        Url = WebUrlBox.Text?.Trim() ?? "",
        Key = WebKeyBox.Text?.Trim() ?? "",
        SongsOnly = WebSongsOnlyBox.IsChecked == true,
    };

    /// <summary>Sends what is on air now to the website, with the settings on screen.</summary>
    async Task TestWeb()
    {
        var web = WebFromUi();
        web.SongsOnly = false; // send whatever is on air
        string station = StationNameBox.Text?.Trim() is { Length: > 0 } name ? name : AppInfo.Name;
        var status = _engine?.GetStatus();
        var onAir = status is { State: not PlayerState.Stopped } st ? st.Current : null;
        WebTestButton.IsEnabled = false;
        WebTestText.Text = L.T("Sending…");
        using var output = new WebNowPlaying(web, station);
        output.Update(onAir, null);
        string? error = await output.SendNowAsync();
        WebTestText.Text = error == null ? L.F("Sent: {0}", (onAir == null ? L.T("nothing on air") : onAir.Display)) : L.F("Failed: {0}", error);
        WebTestButton.IsEnabled = true;
    }

    void ShowFillFolder()
    {
        FillPanel.IsEnabled = FillBox.IsChecked == true;
        FillLibraryPanel.IsEnabled = FillLibraryRadio.IsChecked == true;
        FillFolderText.Text = _fillFolder.Length > 0 ? _fillFolder
            : FillLibraryRadio.IsChecked == true ? L.T("None (optional: used while the library can't be reached)") : L.T("No folder chosen");
        ToolTip.SetTip(FillFolderText, _fillFolder.Length > 0 ? _fillFolder : null);
        ClearFillButton.IsEnabled = _fillFolder.Length > 0;
    }

    readonly Dictionary<long, CheckBox> _fillCategoryBoxes = new();
    List<long> _fillCategoriesUnknown = new(); // chosen, but the library can't be read now: kept as they are

    /// <summary>A tick box per library category (read in the background).</summary>
    async Task LoadFillCategories(List<long> chosen)
    {
        _fillCategoriesUnknown = chosen.ToList();
        if (_main?.MusicLibrary.Db is not { } db)
        {
            FillCategoriesNote.Text = L.T("The library isn't open, so its categories can't be shown. ") +
                (chosen.Count > 0 ? L.T("The categories chosen before are kept.") : L.T("None chosen: every music category."));
            return;
        }
        try
        {
            var categories = await Task.Run(db.Categories);
            foreach (var c in categories)
            {
                var box = new CheckBox { Content = c.Name, IsChecked = chosen.Contains(c.Id) };
                ToolTip.SetTip(box, c.Kind == "music" ? null : L.F("A {0} category", L.T(c.Kind)));
                _fillCategoryBoxes[c.Id] = box;
                FillCategoriesPanel.Children.Add(box);
            }
            _fillCategoriesUnknown.Clear();
        }
        catch (Exception ex)
        {
            FillCategoriesNote.Text = L.F("The library's categories can't be read: {0}", ex.Message);
        }
    }

    List<long> FillCategoriesFromUi() =>
        _fillCategoryBoxes.Count == 0 ? _fillCategoriesUnknown
        : _fillCategoryBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    async Task ChooseFillFolder()
    {
        var start = Directory.Exists(_fillFolder) ? _fillFolder : _settings.MusicFolder;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L.T("Choose the music to play when the playlist runs out"),
            SuggestedStartLocation = Directory.Exists(start) ? await StorageProvider.TryGetFolderFromPathAsync(start) : null,
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _fillFolder = path;
        ShowFillFolder();
    }

    void ShowSignalFile()
    {
        SignalFileText.Text = _signalFile != null ? Path.GetFileName(_signalFile) : L.T("No file chosen");
        ToolTip.SetTip(SignalFileText, _signalFile);
    }

    // ───────────────────────── Colours ─────────────────────────

    PlaylistColors _colors = new();

    /// <summary>Dark tints that keep the playlist's text readable.</summary>
    static readonly string[] Tints =
        ["#16271F", "#1E2A12", "#132A2D", "#18213A", "#1D1A3A", "#261C33", "#2A1B2A", "#2D1A1A", "#2E2414", "#211619", "#1A1D24", "#00000000"];

    /// <summary>A row for each type of playlist item: its colour, presets, a hex box and Default.</summary>
    void BuildColourRows()
    {
        ColourRows.Children.Clear();
        foreach (var kind in PlaylistLook.Kinds)
        {
            var swatch = new Border { Width = 34, Height = 24, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var hex = new TextBox { Width = 110 };
            void Show()
            {
                string value = kind.Get(_colors) ?? kind.Default;
                swatch.Background = new SolidColorBrush(PlaylistLook.Parse(value) ?? Colors.Transparent);
                if (hex.Text != value) hex.Text = value;
            }
            hex.TextChanged += (_, _) =>
            {
                if (PlaylistLook.Parse(hex.Text) is not { }) return;
                kind.Set(_colors, hex.Text!.Trim().Equals(kind.Default, StringComparison.OrdinalIgnoreCase) ? null : hex.Text.Trim());
                Show();
            };
            var presets = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
            foreach (var tint in Tints)
            {
                var b = new Button { Width = 22, Height = 22, Padding = new Thickness(0), Background = new SolidColorBrush(Color.Parse(tint)),
                    BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1) };
                ToolTip.SetTip(b, tint == "#00000000" ? L.T("None") : tint);
                b.Click += (_, _) => { kind.Set(_colors, tint.Equals(kind.Default, StringComparison.OrdinalIgnoreCase) ? null : tint); Show(); };
                presets.Children.Add(b);
            }
            var reset = new Button { Content = L.T("Default") };
            reset.Click += (_, _) => { kind.Set(_colors, null); Show(); };
            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(swatch);
            line.Children.Add(hex);
            line.Children.Add(reset);
            line.Children.Add(presets);
            var row = new StackPanel { Spacing = 4 };
            var label = new TextBlock { Text = L.T(kind.Label) };
            label.Classes.Add("label");
            row.Children.Add(label);
            row.Children.Add(line);
            ColourRows.Children.Add(row);
            Show();
        }
    }

    TimeSignalSettings SignalFromUi() => new(
        (TimeSignalMode)Math.Max(0, TimeSignalModeBox.SelectedIndex),
        SignalFileRadio.IsChecked == true ? _signalFile ?? "" : "",
        _hourFolder,
        (HourAnnouncement)Math.Max(0, HourAtBox.SelectedIndex));

    /// <summary>The hour announcements folder, and which hours have a file.</summary>
    void ShowHourFolder()
    {
        bool set = _hourFolder.Length > 0;
        HourFolderText.Text = set ? _hourFolder : L.T("No folder chosen");
        ToolTip.SetTip(HourFolderText, set ? _hourFolder : null);
        ClearHourFolderButton.IsEnabled = HourAtBox.IsEnabled = set;
        if (!set) { HourFolderNote.Text = ""; return; }
        var missing = Enumerable.Range(0, 24).Where(h => TimeSignal.HourFile(_hourFolder, DateTime.Today.AddHours(h)) == null).ToList();
        HourFolderNote.Text = !Directory.Exists(_hourFolder) ? L.T("The folder isn't there.")
            : missing.Count == 0 ? L.T("A file for every hour.")
            : missing.Count == 24 ? L.T("No hour files found (e.g. HRS05.mp3).")
            : L.F("No file for: {0}", string.Join(", ", missing.Select(h => h.ToString("00"))));
    }

    async Task ChooseSignalFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choose time signal"),
            FileTypeFilter = [new FilePickerFileType(L.T("Audio")) { Patterns = AudioFiles.Patterns }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _signalFile = path;
        SignalFileRadio.IsChecked = true;
        ShowSignalFile();
    }

    void SetLogo(string? path)
    {
        Bitmap? bitmap = null;
        if (path != null)
        {
            try { bitmap = new Bitmap(path); }
            catch { path = null; }
        }
        _logoPath = path;
        (LogoPreview.Source as IDisposable)?.Dispose();
        LogoPreview.Source = bitmap;
        NoLogoText.IsVisible = bitmap == null;
        RemoveLogoButton.IsEnabled = bitmap != null;
    }

    async Task ChooseLogo()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("Choose station logo"),
            FileTypeFilter = [new FilePickerFileType(L.T("Images")) { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SetLogo(path);
    }

    void Save()
    {
        _settings.CheckForUpdates = AutoUpdateBox.IsChecked == true;
        _settings.StationName = StationNameBox.Text?.Trim() ?? "";
        _settings.StationGenre = GenreBox.Text?.Trim() ?? "";
        _settings.StationUrl = UrlBox.Text?.Trim() ?? "";
        _settings.CurrencySign = CurrencyBox.Text?.Trim() ?? "";
        _settings.Language = Localization.Languages[Math.Max(0, LanguageBox.SelectedIndex)].Code;
        _settings.LogoPath = _logoPath;

        _settings.StationType = StationTypeBox.SelectedIndex == 1 ? StationType.Web : StationType.Fm;
        _settings.MicToggle = MicToggleRadio.IsChecked == true;
        _settings.Mic = new MicSettings(
            DeviceName(MicDeviceBox),
            (double)(MicGainBox.Value ?? 0),
            (double)(DuckBox.Value ?? -15),
            (double)(FadeDownBox.Value ?? 0.3m),
            (double)(FadeUpBox.Value ?? 1));

        _settings.TimeSignal = SignalFromUi();

        _settings.Library = LibraryFromUi();
        _settings.Rds = RdsFromUi();
        _settings.WebNowPlaying = WebFromUi();
        _settings.Loudness = LoudnessFromUi();
        _settings.Silence = new SilenceSettings(SilenceBox.IsChecked == true,
            (double)(SilenceLevelBox.Value ?? -45), (double)(SilenceSecondsBox.Value ?? 15));
        _settings.FillSource = FillLibraryRadio.IsChecked == true ? FillSource.Library : FillSource.Folder;
        _settings.FillEnabled = FillBox.IsChecked == true && (_settings.FillSource == FillSource.Library || _fillFolder.Length > 0);
        _settings.FillFolder = _fillFolder;
        _settings.FillCategories = FillCategoriesFromUi();
        _settings.FillSongHours = (double)(FillSongHoursBox.Value ?? 3);
        _settings.FillArtistMinutes = (int)(FillArtistMinutesBox.Value ?? 60);
        _settings.ResumeOnStart = ResumeBox.IsChecked == true;
        _settings.RestartAfterCrash = WatchdogBox.IsChecked == true;
        if (Autostart.IsSupported)
        {
            try { Autostart.Set(AutostartBox.IsChecked == true); }
            catch (Exception ex) { AppLog.Write($"Start at login: {ex.Message}"); }
        }

        _settings.Shortcuts = _keyBoxes.ToDictionary(kv => kv.Key, kv => kv.Value.Gesture?.ToString() ?? "");

        _settings.Crossfade = (double)(CrossfadeBox.Value ?? 0);
        _settings.OtherCrossfade = (double)(OtherCrossfadeBox.Value ?? 0);
        _settings.PlaylistColors = _colors;
        _settings.MusicOutput = RouteFrom(MusicDeviceBox, MusicChannelBox);
        _settings.CartOutput = RouteFrom(CartDeviceBox, CartChannelBox);
        _settings.PreviewOutput = RouteFrom(PreviewDeviceBox, PreviewChannelBox);

        _settings.Shoutcast = ServerTypeBox.SelectedIndex == 1;
        _settings.StreamHost = HostBox.Text?.Trim() ?? "";
        _settings.StreamPort = int.TryParse(PortBox.Text, out var port) ? port : _settings.StreamPort;
        _settings.StreamMount = MountBox.Text?.Trim() ?? "";
        _settings.StreamPassword = PasswordBox.Text ?? "";
        _settings.StreamFormat = Math.Max(0, FormatBox.SelectedIndex);
        _settings.StreamBitrateIndex = Math.Max(0, BitrateBox.SelectedIndex);

        _settings.Save();
        Close(true);
    }
}
