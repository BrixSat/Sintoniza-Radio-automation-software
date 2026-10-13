using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>Station, playout and streaming settings. Edits a copy; <see cref="AppSettings"/> is only changed on Save.</summary>
public partial class SettingsWindow : Window
{
    const string DefaultDevice = "System default";

    readonly AppSettings _settings;
    readonly PlayoutEngine? _engine;
    string? _logoPath;

    // Parameterless constructor for the XAML previewer.
    public SettingsWindow() : this(new AppSettings(), null) { }

    public SettingsWindow(AppSettings settings, PlayoutEngine? engine)
    {
        InitializeComponent();
        _settings = settings;
        _engine = engine;

        StationNameBox.Text = settings.StationName;
        GenreBox.Text = settings.StationGenre;
        UrlBox.Text = settings.StationUrl;
        SetLogo(settings.LogoPath);

        CrossfadeBox.Value = (decimal)settings.Crossfade;
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
        var pairs = Enumerable.Range(0, Math.Max(1, channels / 2)).Select(p => $"Out {p * 2 + 1}–{p * 2 + 2}").ToList();
        channelBox.ItemsSource = pairs;
        channelBox.SelectedIndex = Math.Clamp(firstChannel / 2, 0, pairs.Count - 1);
        channelBox.IsEnabled = pairs.Count > 1;
    }

    static string DeviceName(ComboBox deviceBox) =>
        deviceBox.SelectedItem as string is { } d && d != DefaultDevice ? d : "";

    static OutputRoute RouteFrom(ComboBox deviceBox, ComboBox channelBox) =>
        new(DeviceName(deviceBox), Math.Max(0, channelBox.SelectedIndex) * 2);

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
            Title = "Choose station logo",
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) SetLogo(path);
    }

    void Save()
    {
        _settings.StationName = StationNameBox.Text?.Trim() ?? "";
        _settings.StationGenre = GenreBox.Text?.Trim() ?? "";
        _settings.StationUrl = UrlBox.Text?.Trim() ?? "";
        _settings.LogoPath = _logoPath;

        _settings.Crossfade = (double)(CrossfadeBox.Value ?? 0);
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
