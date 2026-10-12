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
    string? _logoPath;

    // Parameterless constructor for the XAML previewer.
    public SettingsWindow() : this(new AppSettings()) { }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        StationNameBox.Text = settings.StationName;
        GenreBox.Text = settings.StationGenre;
        UrlBox.Text = settings.StationUrl;
        SetLogo(settings.LogoPath);

        CrossfadeBox.Value = (decimal)settings.Crossfade;
        var devices = PlayoutEngine.GetOutputDevices().ToList();
        DeviceBox.ItemsSource = devices.Prepend(DefaultDevice).ToList();
        DeviceBox.SelectedItem = devices.Contains(settings.OutputDevice) ? settings.OutputDevice : DefaultDevice;

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
        _settings.OutputDevice = DeviceBox.SelectedItem as string is { } d && d != DefaultDevice ? d : "";

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
