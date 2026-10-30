using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Playout.Engine;

namespace Playout.App;

/// <summary>Settings → Website: the JSON post, and the files and FTP uploads made from templates.</summary>
public partial class SettingsWindow
{
    /// <summary>The outputs being edited (their rows change them in place).</summary>
    readonly List<MetadataOutput> _webOutputs = new();

    const string WebFields = "{artist} {title} {type} {duration} {started} {next_artist} {next_title} {station} {show} {presenter}";

    void LoadWeb(WebNowPlayingSettings web)
    {
        WebBox.IsChecked = web.Enabled;
        WebUrlBox.Text = web.Url;
        WebKeyBox.Text = web.Key;
        WebSongsOnlyBox.IsChecked = web.SongsOnly;
        WebCoverBox.IsChecked = web.SendCover;
        WebDelayBox.Value = (decimal)Math.Clamp(web.DelaySeconds, 0, 600);
        _webOutputs.Clear();
        _webOutputs.AddRange(web.Outputs.Select(o => o.Clone()));
        ShowWebOutputs();
        WebPanel.IsEnabled = web.Enabled;
        WebBox.IsCheckedChanged += (_, _) => WebPanel.IsEnabled = WebBox.IsChecked == true;
        AddWebTextButton.Click += (_, _) =>
        {
            _webOutputs.Add(new MetadataOutput { Kind = MetadataOutputKind.Text });
            ShowWebOutputs();
        };
        AddWebCoverButton.Click += (_, _) =>
        {
            _webOutputs.Add(new MetadataOutput { Kind = MetadataOutputKind.Cover, Template = "" });
            ShowWebOutputs();
        };
        // Only the on-air program has something to send; Sintoniza Pro (where this tab is) has nothing on air.
        WebTestButton.IsVisible = WebTestText.IsVisible = _engine != null;
        WebTestButton.Click += async (_, _) => await TestWeb();
    }

    WebNowPlayingSettings WebFromUi() => new()
    {
        Enabled = WebBox.IsChecked == true,
        Url = WebUrlBox.Text?.Trim() ?? "",
        Key = WebKeyBox.Text?.Trim() ?? "",
        SongsOnly = WebSongsOnlyBox.IsChecked == true,
        SendCover = WebCoverBox.IsChecked == true,
        DelaySeconds = (double)(WebDelayBox.Value ?? 0),
        Outputs = _webOutputs.Where(o => o.Target.Trim().Length > 0).Select(o => o.Clone()).ToList(),
    };

    /// <summary>A box per output: where it goes, the FTP login, and the template (text) or the picture without a cover.</summary>
    void ShowWebOutputs()
    {
        WebOutputsPanel.Children.Clear();
        foreach (var o in _webOutputs)
        {
            var panel = new StackPanel { Spacing = 6 };
            var head = new DockPanel();
            var remove = new Button { Content = L.T("Remove"), Classes = { "tool" } };
            remove.Click += (_, _) =>
            {
                _webOutputs.Remove(o);
                ShowWebOutputs();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            var enabled = new CheckBox
            {
                Content = o.Kind == MetadataOutputKind.Cover ? L.T("The cover picture") : L.T("A text"),
                IsChecked = o.Enabled, FontWeight = FontWeight.SemiBold,
            };
            enabled.IsCheckedChanged += (_, _) => o.Enabled = enabled.IsChecked == true;
            head.Children.Add(enabled);
            panel.Children.Add(head);

            var target = new TextBox
            {
                Text = o.Target,
                PlaceholderText = o.Kind == MetadataOutputKind.Cover ? "https://www.example.com/nowplaying/nowplaying.php?file=cover.jpg" : "ftp://ftp.example.com/www/nowplaying.txt",
            };
            target.TextChanged += (_, _) => o.Target = target.Text ?? "";
            var choose = new Button { Content = L.T("File…") };
            choose.Click += async (_, _) =>
            {
                var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = L.T("File to write"),
                    SuggestedFileName = o.Kind == MetadataOutputKind.Cover ? "cover.jpg" : "nowplaying.txt",
                    ShowOverwritePrompt = false,
                });
                if (file?.TryGetLocalPath() is { } path) target.Text = SharedPaths.ToLibrary(path); // as every studio sees it
            };
            panel.Children.Add(Labelled(L.T("File, FTP or web address"), WithButton(target, choose)));

            var user = new TextBox { Text = o.User, Width = 200 };
            user.TextChanged += (_, _) => o.User = user.Text ?? "";
            var password = new TextBox { Text = o.Password, Width = 200, PasswordChar = '•' };
            password.TextChanged += (_, _) => o.Password = password.Text ?? "";
            var login = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            login.Children.Add(new TextBlock { Text = L.T("FTP user"), VerticalAlignment = VerticalAlignment.Center });
            login.Children.Add(user);
            login.Children.Add(new TextBlock { Text = L.T("password (web: the key)"), VerticalAlignment = VerticalAlignment.Center });
            login.Children.Add(password);
            panel.Children.Add(login);

            if (o.Kind == MetadataOutputKind.Text)
            {
                var template = new TextBox { Text = o.Template, AcceptsReturn = true, MinHeight = 34, FontFamily = new FontFamily("monospace") };
                template.TextChanged += (_, _) => o.Template = template.Text ?? "";
                panel.Children.Add(Labelled(L.T("Template"), template));
                panel.Children.Add(new TextBlock
                {
                    Classes = { "note" }, TextWrapping = TextWrapping.Wrap,
                    Text = L.F("Fields: {0}. Nothing on air: {1} is the station's name. In a .xml, .html or .json file they are escaped for it.", WebFields, "{title}"),
                });
            }
            else
            {
                var fallback = new TextBox { Text = o.Fallback, PlaceholderText = L.T("None: the picture stays as it was") };
                fallback.TextChanged += (_, _) => o.Fallback = fallback.Text ?? "";
                var pick = new Button { Content = L.T("Choose…") };
                pick.Click += async (_, _) =>
                {
                    var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = L.T("Picture for songs without a cover"),
                        FileTypeFilter = [new FilePickerFileType(L.T("Images")) { Patterns = ["*.png", "*.jpg", "*.jpeg"] }],
                    });
                    if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) fallback.Text = SharedPaths.ToLibrary(path);
                };
                panel.Children.Add(Labelled(L.T("For songs without a cover (e.g. the station's logo)"), WithButton(fallback, pick)));
                panel.Children.Add(new TextBlock
                {
                    Classes = { "note" }, TextWrapping = TextWrapping.Wrap,
                    Text = L.T("The picture is written as it is in the song's file (JPEG or PNG), whatever the name ends with."),
                });
            }
            WebOutputsPanel.Children.Add(new Border
            {
                BorderBrush = Brushes.Gray, BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(10), Child = panel,
            });
        }
    }

    static Control Labelled(string label, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Classes = { "label" }, Text = label });
        panel.Children.Add(control);
        return panel;
    }

    static Control WithButton(Control box, Button button)
    {
        var dock = new DockPanel();
        button.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(button);
        dock.Children.Add(box);
        return dock;
    }

    /// <summary>Sends what is on air now to the website and the outputs, with the settings on screen.</summary>
    async Task TestWeb()
    {
        if (_engine == null) return;
        var web = WebFromUi();
        web.SongsOnly = false; // send whatever is on air
        web.DelaySeconds = 0;
        string station = StationNameBox.Text?.Trim() is { Length: > 0 } name ? name : AppInfo.Name;
        var status = _engine?.GetStatus();
        var onAir = status is { State: not PlayerState.Stopped } st ? st.Current : null;
        WebTestButton.IsEnabled = false;
        WebTestText.Text = L.T("Sending…");
        using var output = new WebNowPlaying(web, station, path => _main?.MusicLibrary.Db?.IsNotForWeb(path) ?? false);
        output.Update(onAir, null);
        string? error = await output.SendNowAsync();
        WebTestText.Text = error == null ? L.F("Sent: {0}", (onAir == null ? L.T("nothing on air") : onAir.Display)) : L.F("Failed: {0}", error);
        WebTestButton.IsEnabled = true;
    }
}
